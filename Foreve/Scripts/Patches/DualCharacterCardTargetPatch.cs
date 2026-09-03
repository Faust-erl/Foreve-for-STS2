using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Foreve.Scripts.DualCharacter;
using STS2RitsuLib;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：非主副角色技能牌/能力牌指定目标角色（2026-08-16 用户需求）。
///
/// 玩法：双人模式下打出**不属于主/副两名角色的技能牌/能力牌**（包括无色牌和
/// 这两名角色以外角色的牌）时，必须先弹窗指定主角色或副角色中一名为目标，
/// 卡牌的对己效果（格挡/增益等 Owner.Creature 效果）按指定角色结算。
/// 攻击牌、主/副角色自己的牌、药水不受影响。
///
/// ── 实现（方法级，版本漂移安全） ─────────────────────────────────────
///   1. 出牌入口拦截：
///      a) PlayCardAction.ExecuteAction（手动出牌，IL 1726885，public virtual Task）——
///         Prefix 判定「需要指定目标」则 return false 跳过原方法，Postfix 把 __result
///         换成 PlayWithTargetChoiceAsync：弹窗选择 → 记录 PendingTargetByCard[card] →
///         重新 Invoke 原方法（Harmony 重入由「已有记录」判定放行）。
///      b) CardCmd.AutoPlay（自动打出，Mayhem/注能/战意难平重放等，IL 1808118，
///         public static Task）——同款拦截。自动打出同样按需求弹窗指定。
///   2. 结算归属：DualCharacterCardOwnerPatch.OnPlayWrapperPrefix 读取
///      PendingTargetByCard → 选副角色时把 _owner 换成副玩家（复用 SwappedOwners /
///      牌堆重定向 / 恢复全套机制，与副角色卡牌同款，轮 8 已实测）；选主角色时
///      本就默认主玩家，无需交换。动作完成后 finally 移除记录。
///   3. 判定（ShouldRequireTarget）：双人模式 && 卡牌是 Skill/Power &&
///      不属于两名角色（CardPool.AllCardIds 未命中，即无色/其他角色牌）&&
///      两名角色都存活（只剩 1 名活人时由既有的死亡重定向自动落给存活角色）。
///   4. 选择弹窗复用 DualCharacterChoiceUi（事件掉血同款头像二选一）。
///
/// ⚠️ 重入保护：PendingTargetByCard 已有记录时不再拦截（重新 Invoke 原方法时放行），
///    同一卡实例的重放（CardCmd.AutoPlay 同名卡）也不会重复弹窗。
/// ⚠️ 记录清理：正常路径由 finally 移除；战斗结束/开始时兜底清空（生命周期订阅）。
///
/// ⚠️ 接线（主流程在 Entry.cs 统一处理，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterCardTargetPatch.Install(Logger);
/// </summary>
public static class DualCharacterCardTargetPatch
{
    private static MegaCrit.Sts2.Core.Logging.Logger? _logger;

    /// <summary>PlayCardAction._card 私有字段（手动出牌动作绑定的卡实例）。</summary>
    private static readonly FieldInfo PlayCardCardField = AccessTools.Field(typeof(PlayCardAction), "_card");

    /// <summary>
    /// 本卡本次出牌指定的目标玩家：出牌动作拦截时写入，OnPlayWrapper 期间由
    /// DualCharacterCardOwnerPatch 读取（选副角色时交换 owner），动作完成后移除。
    /// </summary>
    private static readonly Dictionary<CardModel, Player> PendingTargetByCard = new();

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_card_target");

        // 手动出牌：PlayCardAction.ExecuteAction（public virtual Task，IL 1726885）
        var executeAction = AccessTools.Method(typeof(PlayCardAction), "ExecuteAction");
        if (executeAction != null)
        {
            harmony.Patch(executeAction,
                prefix: new HarmonyMethod(GetMethod(nameof(ExecuteActionPrefix))),
                postfix: new HarmonyMethod(GetMethod(nameof(ExecuteActionPostfix))));
        }

        // 自动打出：CardCmd.AutoPlay(PlayerChoiceContext, CardModel, Creature, AutoPlayType, bool, bool)
        // （public static Task，IL 1808118；Mayhem/注能/战意难平重放等路径）
        var autoPlay = AccessTools.Method(typeof(CardCmd), "AutoPlay",
            new[] { typeof(PlayerChoiceContext), typeof(CardModel), typeof(Creature), typeof(AutoPlayType), typeof(bool), typeof(bool) });
        if (autoPlay != null)
        {
            harmony.Patch(autoPlay,
                prefix: new HarmonyMethod(GetMethod(nameof(AutoPlayPrefix))),
                postfix: new HarmonyMethod(GetMethod(nameof(AutoPlayPostfix))));
        }

        // 兜底清理：战斗结束/开始时清空残留记录（正常路径 finally 已清理）
        RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(
            _ => PendingTargetByCard.Clear(), replayCurrentState: false);
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(
            _ => PendingTargetByCard.Clear(), replayCurrentState: false);

        _logger?.Info($"[Foreve][Dual] 非主副角色技能/能力牌指定目标 patch 已装 " +
                      $"(ExecuteAction={executeAction != null}, CardCmd.AutoPlay={autoPlay != null})");
    }

    /// <summary>读取本卡本次出牌指定的目标玩家（无色/其他角色技能牌/能力牌；无指定返回 null）。</summary>
    public static Player? GetPendingTarget(CardModel card)
        => card != null && PendingTargetByCard.TryGetValue(card, out var player) ? player : null;

    // ── 手动出牌：PlayCardAction.ExecuteAction ─────────────────────────────

    private static bool ExecuteActionPrefix(PlayCardAction __instance, out bool __state)
    {
        __state = false;
        try
        {
            var card = ResolveActionCard(__instance);
            if (card == null || !ShouldRequireTarget(card)) return true;
            __state = true;
            return false; // 跳过原方法，Postfix 注入「选择目标后再执行」的任务
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 出牌目标指定前缀异常: {e}");
            return true;
        }
    }

    private static void ExecuteActionPostfix(PlayCardAction __instance, bool __state,
        MethodInfo __originalMethod, ref Task __result)
    {
        if (!__state) return;
        var card = ResolveActionCard(__instance);
        if (card == null) return;
        __result = PlayWithTargetChoiceAsync(card, () => (Task)__originalMethod.Invoke(__instance, null)!);
    }

    /// <summary>取动作绑定的卡：优先 _card 字段，缺省走 NetCombatCard 解析（网络反序列化路径）。</summary>
    private static CardModel? ResolveActionCard(PlayCardAction action)
    {
        if (action == null) return null;
        try
        {
            var card = (CardModel?)PlayCardCardField?.GetValue(action);
            if (card != null) return card;
            return action.NetCombatCard.ToCardModelOrNull(); // NetCombatCard 是 struct，恒可调用
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 出牌动作卡解析异常: {e}");
            return null;
        }
    }

    // ── 自动打出：CardCmd.AutoPlay ─────────────────────────────────────────

    private static bool AutoPlayPrefix(CardModel card, out bool __state)
    {
        __state = false;
        try
        {
            if (card == null || !ShouldRequireTarget(card)) return true;
            __state = true;
            return false; // 跳过原方法，Postfix 注入「选择目标后再执行」的任务
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 自动打出目标指定前缀异常: {e}");
            return true;
        }
    }

    private static void AutoPlayPostfix(CardModel card, bool __state,
        MethodInfo __originalMethod, object[] __args, ref Task __result)
    {
        if (!__state || card == null) return;
        __result = PlayWithTargetChoiceAsync(card, () => (Task)__originalMethod.Invoke(null, __args)!);
    }

    // ── 共用：判定 + 选择 + 记录 + 重放原方法 ──────────────────────────────

    /// <summary>
    /// 是否需要弹窗指定目标：双人模式 &amp;&amp; 技能/能力牌 &amp;&amp; 不属于两名角色
    /// （无色牌/其他角色牌）&amp;&amp; 两名角色都存活。已有记录（重入/重放）不再拦截。
    /// </summary>
    private static bool ShouldRequireTarget(CardModel card)
    {
        if (card == null) return false;
        if (PendingTargetByCard.ContainsKey(card)) return false; // 重入保护（重新 Invoke 原方法时放行）
        if (!DualCharacterState.Enabled) return false;
        if (card.Type != CardType.Skill && card.Type != CardType.Power) return false; // 只限技能牌/能力牌
        var combatState = card.CombatState ?? card.Owner?.Creature?.CombatState;
        if (combatState == null || !DualCharacterState.IsDualMode(combatState)) return false;
        if (DualCharacterCardOwnerPatch.ResolveCardOwnerPlayer(card) != null) return false; // 主/副角色自己的牌无需指定

        var main = DualCharacterState.MainPlayer;
        var secondary = DualCharacterState.SecondaryPlayer;
        if (main?.Creature == null || secondary?.Creature == null) return false;
        // 只剩 1 名活人时不弹窗（由既有的死亡重定向自动落给存活角色）
        return !main.Creature.IsDead && !secondary.Creature.IsDead;
    }

    /// <summary>弹窗选择目标角色 → 记录 → 重新执行原出牌流程（结束 finally 移除记录）。</summary>
    private static async Task PlayWithTargetChoiceAsync(CardModel card, Func<Task> invokeOriginal)
    {
        var main = DualCharacterState.MainPlayer;
        var secondary = DualCharacterState.SecondaryPlayer;
        Player chosen = main!;

        try
        {
            var candidates = new List<Player>(2);
            if (main?.Creature is { IsDead: false }) candidates.Add(main);
            if (secondary?.Creature is { IsDead: false }) candidates.Add(secondary);

            if (candidates.Count >= 2 && main != null)
            {
                var chosenCreature = await DualCharacterChoiceUi.ShowAsync(
                    "此牌需指定目标角色", candidates, main.Creature!);
                chosen = candidates.Find(p => ReferenceEquals(p.Creature, chosenCreature)) ?? main;
            }
            else if (candidates.Count == 1)
            {
                chosen = candidates[0]; // 只剩 1 名活人：直接指定存活角色，不弹窗
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[Foreve][Dual] 卡牌目标指定弹窗失败，按主角色结算: {ex}");
            chosen = main!;
        }

        PendingTargetByCard[card] = chosen;
        try
        {
            await invokeOriginal();
        }
        finally
        {
            PendingTargetByCard.Remove(card);
        }
    }

    private static MethodInfo GetMethod(string name)
        => typeof(DualCharacterCardTargetPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
}
