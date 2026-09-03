using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：事件改变生命/生命上限自选角色（批次 2a + 2026-08-15 扩展 + 2026-08-23 补全）。
/// 全部 IL 结论来自 C:\tmp\sts2_full.il（2026-08-14 / 2026-08-15 / 2026-08-23 实证）。
///
/// 玩法：双人模式下，事件（mod 事件 + 原版事件）造成掉血、失去最大生命、回血或增加最大生命时，
/// 弹选择由玩家自选一名角色承受/获得。四类共用同一头像选择弹窗与结算方式。
///
/// ── IL 调研结论（本批次实证，写码依据） ──────────────────────────────────────
///   1. 掉血链：事件选项执行时对玩家造成伤害 = CreatureCmd.Damage(PlayerChoiceContext, Creature,
///      ...) 单目标重载，ctx 用 no-op 的 ThrowingPlayerChoiceContext
///      （mod 事件 OgierKnightsTrial 实证：`await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(),
///      Owner!.Creature, 7, ValueProp.Unblockable | ValueProp.Unpowered, null, null)`；
///      原版事件 AbyssalBaths Immerse 同款，IL 1424100 区域 newobj ThrowingPlayerChoiceContext）。
///      CreatureCmd.Damage 全部 10 个重载（IL 1154444 等）：6 个单目标（2 参=Creature）+
///      4 个 IEnumerable（AOE）。战斗内伤害的 ctx 是真实战斗上下文（CardPlay 等），
///      不会被误拦截 —— 判定条件「ctx is ThrowingPlayerChoiceContext」即等价于「战斗外事件掉血」。
///   1b. 失去最大生命：CreatureCmd.LoseMaxHp(PlayerChoiceContext, Creature, decimal, bool)
///       （IL 1826783），事件路径同样用 ThrowingPlayerChoiceContext（mod 事件 OgierPrincessGift
///       实证：`CreatureCmd.LoseMaxHp(new ThrowingPlayerChoiceContext(), Owner!.Creature, 5, false)`）。
///   1c. 增加生命（回血）：CreatureCmd.Heal(Creature, decimal, bool)（IL 1826696，唯一重载，
///       返回 Task）——事件路径没有上下文参数，直接传 Owner.Creature（mod 事件
///       OgierKnightsTrial 离开选项实证：`CreatureCmd.Heal(Owner!.Creature, 10, false)`；
///       原版事件同款）。事件判定只能看调用链：原版事件类全部直接/间接继承
///       MegaCrit.Sts2.Core.Models.EventModel（AbyssalBaths IL 1423342 实证），mod 事件经
///       ModEventTemplate : EventModel（RitsuLib 源码实证）→ 调用链任一帧的 DeclaringType 链上
///       出现 EventModel 派生类即事件上下文。卡牌（Feed 等）/遗物/药水/篝火（RestSiteOption）/
///       复活（DualCharacterRevive）都不是 EventModel 派生，不会误拦截。
///   1d. 增加最大生命：CreatureCmd.GainMaxHp(Creature, decimal)（IL 1826755，唯一重载，
///       返回 Task），同样无上下文参数（原版事件 AbyssalBaths 浸浴 / ByrdonisNest 吃 /
///       EndlessConveyor 鱼子酱 / MorphicGrove 独行者 / StoneOfAllTime 举石 /
///       WaterloggedScriptorium 血墨实证，IL 1424065 等），拦截条件同 1c。
///   2. 选择 UI：游戏/RitsuLib 无现成的「选一名玩家」弹窗 API（调研 ritsulib_src 全部 Contract
///      与 Choice/Selection 类，仅有战斗内 AnyPlayer 卡牌目标箭头，NCombatUi 系）。
///      最简可行方案 = mod 自建纯代码 Godot 弹窗（CanvasLayer + 半透明遮罩 + 角色头像按钮，
///      TaskCompletionSource 等待选择），零场景资源、零本地化依赖。
///      2026-08-15 用户需求：两个文字选项改为两枚角色头像（左上角角色头像 48×48 的 2.5 倍 =
///      120×120），标题「事件掉血：由谁承受？」同步放大 2.5 倍。
///      2026-08-16：选择弹窗提取为共享类 DualCharacterChoiceUi（事件掉血/失去最大生命/
///      无色或其他角色技能牌能力牌指定目标角色共用）；2026-08-23 起事件回血/
///      增加最大生命复用同款弹窗。
///   3. Harmony 实现：Prefix 无法 await，故用「Prefix 拦截(return false) + Postfix 注入 __result」
///      模式：Prefix 捕获原参数（__args）与 __originalMethod，异步弹窗选择后以被选角色的
///      Creature 替换 __args[0]（Heal/GainMaxHp）或 __args[1]（Damage/LoseMaxHp）再 Invoke
///      原方法（_applyingChoice 防递归重拦）。
///   4. 死亡角色不可被选为承受者（选了也白选）；只剩 1 名活人时不弹窗直接承受；
///      若被选者因此死亡属预期（双人局单死不判负，批次 1b 已实现，篝火/新层会复活）。
///
/// ⚠️ 接线（主流程统一在 Entry.cs 处理，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterEventPatches.Install(Logger);
/// </summary>
public static class DualCharacterEventPatches
{
    private static MegaCrit.Sts2.Core.Logging.Logger? _logger;

    /// <summary>正在把已选伤害写回原方法（防递归重拦）。</summary>
    private static bool _applyingChoice;

    /// <summary>本次 Damage 调用已被拦截（Postfix 据此注入 __result）。</summary>
    private static bool _intercepted;

    /// <summary>拦截后异步执行的选择+伤害任务（Postfix 注入给调用方 await）。</summary>
    private static Task<IEnumerable<DamageResult>>? _pendingTask;

    /// <summary>本次 LoseMaxHp 调用已被拦截（Postfix 据此注入 __result）。</summary>
    private static bool _interceptedMaxHp;

    /// <summary>拦截后异步执行的选择+失去最大生命任务。</summary>
    private static Task? _pendingMaxHpTask;

    /// <summary>本次 Heal 调用已被拦截（Postfix 据此注入 __result）。</summary>
    private static bool _interceptedHeal;

    /// <summary>拦截后异步执行的选择+回血任务。</summary>
    private static Task? _pendingHealTask;

    /// <summary>本次 GainMaxHp 调用已被拦截（Postfix 据此注入 __result）。</summary>
    private static bool _interceptedGainMaxHp;

    /// <summary>拦截后异步执行的选择+增加最大生命任务。</summary>
    private static Task? _pendingGainMaxHpTask;

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_event_damage");

        // 全部单目标 Damage 重载（第 2 参 = Creature）：mod/原版事件掉血都走这里
        var methods = AccessTools.GetDeclaredMethods(typeof(CreatureCmd))
            .Where(m => m.Name == "Damage"
                        && m.GetParameters().Length >= 2
                        && m.GetParameters()[1].ParameterType == typeof(Creature))
            .ToList();

        foreach (var m in methods)
        {
            harmony.Patch(m,
                prefix: new HarmonyMethod(GetMethod(nameof(DamagePrefix))),
                postfix: new HarmonyMethod(GetMethod(nameof(DamagePostfix))));
        }

        // 失去最大生命事件与掉血事件同款结算（IL 1826783，唯一重载）
        var loseMaxHp = AccessTools.Method(typeof(CreatureCmd), "LoseMaxHp",
            new[] { typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(bool) });
        if (loseMaxHp != null)
        {
            harmony.Patch(loseMaxHp,
                prefix: new HarmonyMethod(GetMethod(nameof(LoseMaxHpPrefix))),
                postfix: new HarmonyMethod(GetMethod(nameof(LoseMaxHpPostfix))));
        }

        // 回血事件（IL 1826696，唯一重载；无上下文参数，事件判定看调用链）
        var heal = AccessTools.Method(typeof(CreatureCmd), "Heal",
            new[] { typeof(Creature), typeof(decimal), typeof(bool) });
        if (heal != null)
        {
            harmony.Patch(heal,
                prefix: new HarmonyMethod(GetMethod(nameof(HealPrefix))),
                postfix: new HarmonyMethod(GetMethod(nameof(HealPostfix))));
        }

        // 增加最大生命事件（IL 1826755，唯一重载；无上下文参数，事件判定看调用链）
        var gainMaxHp = AccessTools.Method(typeof(CreatureCmd), "GainMaxHp",
            new[] { typeof(Creature), typeof(decimal) });
        if (gainMaxHp != null)
        {
            harmony.Patch(gainMaxHp,
                prefix: new HarmonyMethod(GetMethod(nameof(GainMaxHpPrefix))),
                postfix: new HarmonyMethod(GetMethod(nameof(GainMaxHpPostfix))));
        }

        _logger?.Info($"[Foreve][Dual] 事件掉血/失去最大生命/回血/增加最大生命自选 patch 已装 " +
                      $"({methods.Count} 个单目标 Damage 重载, LoseMaxHp={loseMaxHp != null}, " +
                      $"Heal={heal != null}, GainMaxHp={gainMaxHp != null})");
    }

    /// <summary>收集可选的承受/获得角色（双人局且两名角色都存活时才有选择；
    /// 死亡角色不可选，只剩 1 名活人时不弹窗直接承受）。</summary>
    private static bool TryGetChoiceCandidates(Creature? target, out List<Player> candidates)
    {
        candidates = new List<Player>(2);
        if (!DualCharacterState.Enabled || target == null || !target.IsPlayer)
            return false;

        var main = DualCharacterState.MainPlayer;
        var secondary = DualCharacterState.SecondaryPlayer;
        if (main?.Creature == null || secondary?.Creature == null) return false;

        if (!main.Creature.IsDead) candidates.Add(main);
        if (!secondary.Creature.IsDead) candidates.Add(secondary);
        return candidates.Count >= 2;
    }

    /// <summary>掉血/失去最大生命专用：额外要求事件上下文（ThrowingPlayerChoiceContext）。
    /// 战斗内伤害的 ctx 是真实战斗上下文，不会被误拦截。</summary>
    private static bool TryGetChoiceCandidates(PlayerChoiceContext? ctx, Creature? target, out List<Player> candidates)
    {
        candidates = new List<Player>(2);
        if (ctx is not ThrowingPlayerChoiceContext) return false;
        return TryGetChoiceCandidates(target, out candidates);
    }

    /// <summary>
    /// 调用链判定「事件上下文」：Heal/GainMaxHp 没有 PlayerChoiceContext 参数，事件路径
    /// （原版事件类、mod 事件的 ModEventTemplate）全部直接/间接继承
    /// MegaCrit.Sts2.Core.Models.EventModel，因此遍历调用链找 EventModel 派生类即可隔离事件。
    /// 异步选项的 MoveNext 的 DeclaringType 是「事件类+&lt;选项名&gt;d__N」嵌套类型，
    /// 沿 DeclaringType 链向上同样能到达事件类。卡牌/遗物/药水/篝火/复活都不是 EventModel 派生，
    /// 不会被误判成事件。
    /// </summary>
    private static bool IsEventContext()
    {
        try
        {
            var trace = new StackTrace(false);
            // frame 0 是 Prefix 自身，从 1 开始找调用者
            for (var i = 1; i < trace.FrameCount; i++)
            {
                var type = trace.GetFrame(i)?.GetMethod()?.DeclaringType;
                while (type != null)
                {
                    if (typeof(MegaCrit.Sts2.Core.Models.EventModel).IsAssignableFrom(type))
                        return true;
                    type = type.DeclaringType;
                }
            }
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] 事件上下文调用链判定失败: {e.Message}");
        }
        return false;
    }

    /// <summary>
    /// 拦截条件：双人模式 && 事件上下文（ThrowingPlayerChoiceContext）&& 目标是玩家角色
    /// && 两名角色都存活。满足 → 弹选择并跳过原方法（Postfix 注入任务）。
    /// </summary>
    private static bool DamagePrefix(object[] __args, MethodInfo __originalMethod)
    {
        if (_applyingChoice || _pendingTask != null || _pendingMaxHpTask != null
            || _pendingHealTask != null || _pendingGainMaxHpTask != null) return true;
        if (__args == null || __args.Length < 2) return true;

        var ctx = __args[0] as PlayerChoiceContext;
        var target = __args[1] as Creature;
        if (!TryGetChoiceCandidates(ctx, target, out var candidates)) return true;

        _intercepted = true;
        _pendingTask = ApplyChosenDamageAsync(ctx, target, __args, __originalMethod, candidates);
        return false;
    }

    /// <summary>被拦截时把异步任务注入 __result（原方法被跳过，__result 为 null）。</summary>
    private static void DamagePostfix(ref Task<IEnumerable<DamageResult>> __result)
    {
        if (!_intercepted) return;
        _intercepted = false;
        __result = _pendingTask ?? Task.FromResult<IEnumerable<DamageResult>>(Array.Empty<DamageResult>());
        _pendingTask = null;
    }

    /// <summary>弹窗选择承受者 → 替换目标 → 调原方法真正造成伤害。</summary>
    private static async Task<IEnumerable<DamageResult>> ApplyChosenDamageAsync(
        PlayerChoiceContext ctx, Creature originalTarget, object[] args, MethodInfo originalMethod,
        List<Player> candidates)
    {
        Creature chosen;
        try
        {
            chosen = await DualCharacterChoiceUi.ShowAsync("事件掉血：由谁承受？", candidates, originalTarget);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[Foreve][Dual] 事件掉血选择弹窗失败，由原目标承受: {ex}");
            chosen = originalTarget;
        }

        args[1] = chosen;
        _applyingChoice = true;
        try
        {
            var result = (Task<IEnumerable<DamageResult>>)originalMethod.Invoke(null, args)!;
            return await result;
        }
        finally
        {
            _applyingChoice = false;
        }
    }

    /// <summary>失去最大生命：与掉血事件相同的拦截条件与选择弹窗。</summary>
    private static bool LoseMaxHpPrefix(object[] __args, MethodInfo __originalMethod)
    {
        if (_applyingChoice || _pendingMaxHpTask != null || _pendingTask != null
            || _pendingHealTask != null || _pendingGainMaxHpTask != null) return true;
        if (__args == null || __args.Length < 2) return true;

        var ctx = __args[0] as PlayerChoiceContext;
        var target = __args[1] as Creature;
        if (!TryGetChoiceCandidates(ctx, target, out var candidates)) return true;

        _interceptedMaxHp = true;
        _pendingMaxHpTask = ApplyChosenMaxHpAsync(ctx, target, __args, __originalMethod, candidates);
        return false;
    }

    /// <summary>LoseMaxHp 被拦截时把选择+结算任务注入 __result。</summary>
    private static void LoseMaxHpPostfix(ref Task __result)
    {
        if (!_interceptedMaxHp) return;
        _interceptedMaxHp = false;
        __result = _pendingMaxHpTask ?? Task.CompletedTask;
        _pendingMaxHpTask = null;
    }

    /// <summary>弹窗选择承受者 → 替换 creature → 调原方法失去最大生命。</summary>
    private static async Task ApplyChosenMaxHpAsync(
        PlayerChoiceContext ctx, Creature originalTarget, object[] args, MethodInfo originalMethod,
        List<Player> candidates)
    {
        Creature chosen;
        try
        {
            chosen = await DualCharacterChoiceUi.ShowAsync("事件掉血：由谁承受？", candidates, originalTarget);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[Foreve][Dual] 失去最大生命选择弹窗失败，由原目标承受: {ex}");
            chosen = originalTarget;
        }

        args[1] = chosen;
        _applyingChoice = true;
        try
        {
            var result = (Task)originalMethod.Invoke(null, args)!;
            await result;
        }
        finally
        {
            _applyingChoice = false;
        }
    }

    /// <summary>
    /// 回血事件：Heal 无上下文参数，用调用链判断事件上下文（IsEventContext）；
    /// 卡牌/遗物/药水/篝火/复活等非事件调用一律放行。满足拦截条件
    /// （双人模式 && 事件 && 目标是玩家角色 && 两名角色都存活）→ 弹选择并跳过原方法。
    /// </summary>
    private static bool HealPrefix(object[] __args, MethodInfo __originalMethod)
    {
        if (_applyingChoice || _pendingHealTask != null || _pendingTask != null
            || _pendingMaxHpTask != null || _pendingGainMaxHpTask != null) return true;
        if (__args == null || __args.Length < 1) return true;

        var target = __args[0] as Creature;
        if (!DualCharacterState.Enabled || target == null || !target.IsPlayer) return true;
        if (!IsEventContext()) return true;
        if (!TryGetChoiceCandidates(target, out var candidates)) return true;

        _interceptedHeal = true;
        _pendingHealTask = ApplyChosenHealAsync(target, __args, __originalMethod, candidates);
        return false;
    }

    /// <summary>Heal 被拦截时把选择+结算任务注入 __result。</summary>
    private static void HealPostfix(ref Task __result)
    {
        if (!_interceptedHeal) return;
        _interceptedHeal = false;
        __result = _pendingHealTask ?? Task.CompletedTask;
        _pendingHealTask = null;
    }

    /// <summary>弹窗选择获得回血的角色 → 替换 creature → 调原方法真正回血。</summary>
    private static async Task ApplyChosenHealAsync(
        Creature originalTarget, object[] args, MethodInfo originalMethod, List<Player> candidates)
    {
        Creature chosen;
        try
        {
            chosen = await DualCharacterChoiceUi.ShowAsync("事件回血：由谁获得？", candidates, originalTarget);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[Foreve][Dual] 事件回血选择弹窗失败，由原目标承受: {ex}");
            chosen = originalTarget;
        }

        args[0] = chosen;
        _applyingChoice = true;
        try
        {
            var result = (Task)originalMethod.Invoke(null, args)!;
            await result;
        }
        finally
        {
            _applyingChoice = false;
        }
    }

    /// <summary>
    /// 增加最大生命事件：GainMaxHp 无上下文参数，与回血同款的事件调用链判定与选择弹窗。
    /// </summary>
    private static bool GainMaxHpPrefix(object[] __args, MethodInfo __originalMethod)
    {
        if (_applyingChoice || _pendingGainMaxHpTask != null || _pendingTask != null
            || _pendingMaxHpTask != null || _pendingHealTask != null) return true;
        if (__args == null || __args.Length < 1) return true;

        var target = __args[0] as Creature;
        if (!DualCharacterState.Enabled || target == null || !target.IsPlayer) return true;
        if (!IsEventContext()) return true;
        if (!TryGetChoiceCandidates(target, out var candidates)) return true;

        _interceptedGainMaxHp = true;
        _pendingGainMaxHpTask = ApplyChosenGainMaxHpAsync(target, __args, __originalMethod, candidates);
        return false;
    }

    /// <summary>GainMaxHp 被拦截时把选择+结算任务注入 __result。</summary>
    private static void GainMaxHpPostfix(ref Task __result)
    {
        if (!_interceptedGainMaxHp) return;
        _interceptedGainMaxHp = false;
        __result = _pendingGainMaxHpTask ?? Task.CompletedTask;
        _pendingGainMaxHpTask = null;
    }

    /// <summary>弹窗选择获得生命上限的角色 → 替换 creature → 调原方法真正增加最大生命。</summary>
    private static async Task ApplyChosenGainMaxHpAsync(
        Creature originalTarget, object[] args, MethodInfo originalMethod, List<Player> candidates)
    {
        Creature chosen;
        try
        {
            chosen = await DualCharacterChoiceUi.ShowAsync("事件增加生命上限：由谁获得？", candidates, originalTarget);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[Foreve][Dual] 增加最大生命选择弹窗失败，由原目标承受: {ex}");
            chosen = originalTarget;
        }

        args[0] = chosen;
        _applyingChoice = true;
        try
        {
            var result = (Task)originalMethod.Invoke(null, args)!;
            await result;
        }
        finally
        {
            _applyingChoice = false;
        }
    }

    // ── 工具 ──────────────────────────────────────────────────────────────

    private static MethodInfo GetMethod(string name)
        => typeof(DualCharacterEventPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
}