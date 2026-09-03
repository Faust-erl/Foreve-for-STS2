using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Combat;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：战斗内副玩家（后排角色）creature 血条/格挡常态显示（2026-08-14 用户实测修复）。
///
/// ⚠️ 根因（C:\tmp\sts2_full.il 实证）：
///   NCreature._Ready（IL 959830~960032）计算 _isRemotePlayerOrPet =
///     (entity.IsPlayer &amp;&amp; !LocalContext.IsMe(entity)) || (PetOwner != null &amp;&amp; !LocalContext.IsMe(PetOwner))
///   （IL_00e4~IL_0212）；为 true（=联机「远程玩家/宠物」）时调用 _stateDisplay.HideImmediately()
///   （IL_0217~IL_0225）→ 血条/格挡默认隐藏。
///   悬停显示/移开隐藏走 OnFocus/OnUnfocus（private，IL 960655~960890）：
///     OnFocus 远程分支 → _stateDisplay.AnimateIn(HealthBarAnimMode.FromHidden) + ZIndex=1；
///     OnUnfocus 远程分支 → _stateDisplay.AnimateOut()。
///   双人局副玩家 creature：IsPlayer=true 且 IsMe=false → 被判定为远程玩家 → 血条/格挡悬停才显示。
///   （IsMe 判定与 GetMe patch 无关：GetMe 返回主玩家，IsMe(副玩家 creature)=false 原样成立。）
///
/// 修复（全部仅双人模式生效；单玩家/真联机多人零变化）：
///   1. NCreature._Ready Postfix：副玩家 creature 创建后立即 AnimateIn(FromHidden) + ZIndex=1
///      （完全镜像 OnFocus 远程分支的显示效果）→ 常显血条/格挡；
///   2. NCreature.OnUnfocus Postfix：原方法对远程玩家 AnimateOut 后，副玩家重新 AnimateIn
///      （悬停移开不再隐藏）；
///   3. NCreature.SetRemotePlayerFocused Prefix：双人模式直接跳过（no-op）——
///      原方法在悬停副玩家时会把本地（主玩家）血条 AnimateOut 藏掉、移开再恢复；
///      双人模式两名角色血条都常显，不需要这套「焦点切换」隐藏逻辑。
///      （OnCreatureDied 只把血条 hitbox 的 MouseFilter 设为 Ignore，不隐藏血条，死亡后仍显示 0 HP，无冲突。）
///
/// ⚠️ 接线（主流程在 Entry.cs 统一处理，不要直接改 Entry.cs）：
///   本 Install 已在 DualCharacterCombatPatches.Install(Logger) 末尾调用（该 Install 已由 Entry.cs 接线），
///   无需改 Entry.cs；若未来独立接线：Foreve.Scripts.Patches.DualCharacterCombatUiPatch.Install(Logger);
/// </summary>
public static class DualCharacterCombatUiPatch
{
    private static MegaCrit.Sts2.Core.Logging.Logger _logger = null!;

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_combat_ui");

        var ready = AccessTools.Method(typeof(NCreature), "_Ready");
        var onUnfocus = AccessTools.Method(typeof(NCreature), "OnUnfocus");
        var setRemoteFocused = AccessTools.Method(typeof(NCreature), "SetRemotePlayerFocused",
            new[] { typeof(bool) });
        var hideImmediately = AccessTools.Method(typeof(NCreatureStateDisplay), "HideImmediately");
        var animateOut = AccessTools.Method(typeof(NCreatureStateDisplay), "AnimateOut");

        if (ready == null)
        {
            _logger.Warn("[Foreve][Dual] NCreature._Ready NOT FOUND - skip secondary state display always-on");
        }
        else
        {
            harmony.Patch(ready, postfix: new HarmonyMethod(
                typeof(DualCharacterCombatUiPatch).GetMethod(nameof(CreatureReadyPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
        }

        if (onUnfocus == null)
        {
            _logger.Warn("[Foreve][Dual] NCreature.OnUnfocus NOT FOUND - skip secondary hover-out keep-visible");
        }
        else
        {
            harmony.Patch(onUnfocus, postfix: new HarmonyMethod(
                typeof(DualCharacterCombatUiPatch).GetMethod(nameof(CreatureOnUnfocusPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
        }

        if (setRemoteFocused == null)
        {
            _logger.Warn("[Foreve][Dual] NCreature.SetRemotePlayerFocused NOT FOUND - skip local bar keep-visible");
        }
        else
        {
            harmony.Patch(setRemoteFocused, prefix: new HarmonyMethod(
                typeof(DualCharacterCombatUiPatch).GetMethod(nameof(SetRemotePlayerFocusedPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
        }

        // 兜底：NCreatureStateDisplay 层面拦截副玩家的 HideImmediately/AnimateOut——
        // 除 _Ready/OnUnfocus 外，GDScript（InvokeGodotClassMethod 分发）也可能调用这两个方法，
        // 双人模式下副玩家血条任何路径都禁止隐藏。
        if (hideImmediately == null)
        {
            _logger.Warn("[Foreve][Dual] NCreatureStateDisplay.HideImmediately NOT FOUND - skip hide guard");
        }
        else
        {
            harmony.Patch(hideImmediately, prefix: new HarmonyMethod(
                typeof(DualCharacterCombatUiPatch).GetMethod(nameof(StateDisplayHidePrefix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
        }

        if (animateOut == null)
        {
            _logger.Warn("[Foreve][Dual] NCreatureStateDisplay.AnimateOut NOT FOUND - skip animate-out guard");
        }
        else
        {
            harmony.Patch(animateOut, prefix: new HarmonyMethod(
                typeof(DualCharacterCombatUiPatch).GetMethod(nameof(StateDisplayHidePrefix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
        }

        _logger.Info($"[Foreve][Dual] 副玩家 creature 血条/格挡常显 patch 已安装 (_Ready={ready != null}, OnUnfocus={onUnfocus != null}, SetRemotePlayerFocused={setRemoteFocused != null}, HideImmediately/AnimateOut guard={hideImmediately != null && animateOut != null})");
    }

    /// <summary>副玩家 creature 判定：双人模式 &amp;&amp; creature 属于副玩家（含宠物/召唤物自动排除——宠物 IsPlayer=false）。</summary>
    private static bool IsSecondaryPlayerCreature(NCreature? node)
    {
        if (!DualCharacterState.Enabled || node == null) return false;
        var entity = node.Entity;
        if (entity == null || !entity.IsPlayer) return false;
        var combat = entity.CombatState;
        if (combat == null || !DualCharacterState.IsDualMode(combat)) return false;
        return DualCharacterState.IsSecondaryPlayer(entity.Player);
    }

    /// <summary>取 NCreature 的私有字段 _stateDisplay（NCreatureStateDisplay，public 方法可直接调用）。</summary>
    private static NCreatureStateDisplay? GetStateDisplay(NCreature node)
    {
        try
        {
            return Traverse.Create(node).Field("_stateDisplay").GetValue<NCreatureStateDisplay>();
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 获取 _stateDisplay 失败: {e.Message}");
            return null;
        }
    }

    /// <summary>常显副玩家血条/格挡：镜像 OnFocus 远程分支（AnimateIn(FromHidden) + ZIndex=1）。</summary>
    private static void CreatureReadyPostfix(NCreature __instance)
    {
        try
        {
            if (!IsSecondaryPlayerCreature(__instance)) return;
            var display = GetStateDisplay(__instance);
            if (display == null) return;
            display.AnimateIn(HealthBarAnimMode.FromHidden);
            display.ZIndex = 1;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] _Ready 副玩家血条常显异常: {e.Message}");
        }
    }

    /// <summary>悬停移开（OnUnfocus 已对远程玩家 AnimateOut）后重新显示副玩家血条/格挡。</summary>
    private static void CreatureOnUnfocusPostfix(NCreature __instance)
    {
        try
        {
            if (!IsSecondaryPlayerCreature(__instance)) return;
            var display = GetStateDisplay(__instance);
            if (display == null) return;
            display.AnimateIn(HealthBarAnimMode.FromHidden);
            display.ZIndex = 1;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] OnUnfocus 副玩家血条常显异常: {e.Message}");
        }
    }

    /// <summary>双人模式：SetRemotePlayerFocused 整体跳过（悬停副玩家时不再隐藏主玩家血条）。</summary>
    private static bool SetRemotePlayerFocusedPrefix()
    {
        return !DualCharacterState.Enabled;
    }

    /// <summary>
    /// 双人模式副玩家：拦截 NCreatureStateDisplay.HideImmediately/AnimateOut（return false 跳过原方法）。
    /// 判定用状态显示内部 _creature 字段（NCreatureStateDisplay.SetCreature 已写入，IL 966596~966640 实证）；
    /// 非双人模式/非副玩家（怪物/主玩家/宠物）一律放行，行为零变化。
    /// </summary>
    private static bool StateDisplayHidePrefix(NCreatureStateDisplay __instance)
    {
        if (!DualCharacterState.Enabled) return true;
        Creature? creature = null;
        try { creature = Traverse.Create(__instance).Field("_creature").GetValue<Creature>(); }
        catch { return true; }
        if (creature == null || !creature.IsPlayer) return true;
        var combat = creature.CombatState;
        if (combat == null || !DualCharacterState.IsDualMode(combat)) return true;
        return !DualCharacterState.IsSecondaryPlayer(creature.Player);
    }
}
