using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：能量共用修复（2026-08-16 用户反馈）。
///
/// 需求：能量是共用的，没有主/副角色自身的能量；「获得能量」的效果均应增加共用能量。
///
/// 根因（IL/反编译实证）：
///   - 卡牌 OnPlay 执行期间 DualCharacterCardOwnerPatch 会把 CardModel._owner 临时换成
///     卡牌所属角色玩家（副角色牌 → 副玩家），OnPlay 结束才恢复。
///   - 原版「播种」附魔 Sown.OnPlay 在 OnPlayWrapper 内（OnPlay 之后）执行：
///       PlayerCmd.GainEnergy(Amount, card.Owner)
///     → 能量加到副玩家 PlayerCombatState（MaxEnergy=0、无 UI、不参与共用）→ 共用能量不涨。
///   - mod 卡 RotanBehemothRampage（巨兽暴走）的 PlayerCmd.GainEnergy(1, Owner) 同款问题。
///   - 费用扣除（CardModel.SpendResources/SpendEnergy → PCS.LoseEnergy）发生在
///     PlayCardAction（OnPlayWrapper 之前，owner 仍是主玩家），本就不受影响。
///
/// 方案：patch PlayerCombatState.GainEnergy / LoseEnergy（游戏内能量增减的唯一最终漏斗，
///   PlayerCmd.GainEnergy/LoseEnergy 与 CardModel.SpendEnergy 全部汇入这里）：
///   双人模式下，副玩家 PCS 的能量增减一律重定向到主玩家 PCS（共用池）→
///   播种/巨兽暴走等效果的能量 +1 正确显示在共用能量上；消耗类效果也不会因为
///   副玩家池为 0 而「免费」。
///
/// ⚠️ 刻意不 patch ResetEnergy / AddMaxEnergyToCurrent（回合开始的重置/叠加路径）：
///   若把副玩家回合开始的能量重置也重定向到主玩家，共用能量每轮会回满两次（双倍能量）。
///
/// 仅双人模式生效（DualCharacterState.Enabled + IsSecondaryPlayer 双重判定）；
/// 单玩家局/真联机多人零变化；找不到方法/字段只 Warn 不崩（版本漂移容错）。
///
/// ⚠️ 接线（主流程统一接 Entry.cs，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterEnergyPatch.Install(Logger);
/// </summary>
public static class DualCharacterEnergyPatch
{
    private static MegaCrit.Sts2.Core.Logging.Logger? _logger;

    /// <summary>PlayerCombatState._player 私有字段（PCS → Player 归属判定用）。</summary>
    private static readonly FieldInfo PcsPlayerField = AccessTools.Field(typeof(PlayerCombatState), "_player");

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_energy");

        var gainEnergy = AccessTools.Method(typeof(PlayerCombatState), "GainEnergy", new[] { typeof(decimal) });
        if (gainEnergy != null)
        {
            harmony.Patch(gainEnergy, prefix: new HarmonyMethod(GetMethod(nameof(GainEnergyPrefix))));
        }

        var loseEnergy = AccessTools.Method(typeof(PlayerCombatState), "LoseEnergy", new[] { typeof(decimal) });
        if (loseEnergy != null)
        {
            harmony.Patch(loseEnergy, prefix: new HarmonyMethod(GetMethod(nameof(LoseEnergyPrefix))));
        }

        _logger?.Info($"[Foreve][Dual] 共用能量重定向 patch 已装 (GainEnergy={gainEnergy != null}, LoseEnergy={loseEnergy != null}, PcsPlayerField={PcsPlayerField != null})");
    }

    /// <summary>副玩家获得能量 → 重定向到主玩家（共用能量）；返回 false 拦截原方法。</summary>
    private static bool GainEnergyPrefix(PlayerCombatState __instance, decimal amount)
    {
        return RedirectIfSecondary(__instance, amount, isGain: true);
    }

    /// <summary>副玩家失去能量 → 重定向到主玩家（共用能量）；返回 false 拦截原方法。</summary>
    private static bool LoseEnergyPrefix(PlayerCombatState __instance, decimal amount)
    {
        return RedirectIfSecondary(__instance, amount, isGain: false);
    }

    /// <summary>
    /// 双人模式下把副玩家 PCS 的能量增减重定向到主玩家 PCS。
    /// 非双人 / 非副玩家 / 主玩家未就绪：返回 true 走原逻辑（零变化）。
    /// </summary>
    private static bool RedirectIfSecondary(PlayerCombatState __instance, decimal amount, bool isGain)
    {
        try
        {
            if (__instance == null || amount <= 0m) return true;
            if (!DualCharacterState.Enabled) return true;

            var player = PcsPlayerField?.GetValue(__instance) as Player;
            if (player == null || !DualCharacterState.IsSecondaryPlayer(player)) return true;

            var mainPcs = DualCharacterState.MainPlayer?.PlayerCombatState;
            if (mainPcs == null || ReferenceEquals(mainPcs, __instance)) return true; // 防御：主副引用异常时避免自递归

            if (isGain) mainPcs.GainEnergy(amount);
            else mainPcs.LoseEnergy(amount);
            return false;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 共用能量重定向异常: {e.Message}");
            return true;
        }
    }

    private static MethodInfo GetMethod(string name)
        => typeof(DualCharacterEnergyPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
}
