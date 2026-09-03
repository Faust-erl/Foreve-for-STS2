using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.RunData;
using MegaCrit.Sts2.Core.Logging;
using Foreve.Scripts.Data;

namespace Foreve.Scripts.DualCharacter;

/// <summary>
/// 双角色模式：遗物「装备角色」系统（2026-08-18 用户新规格，遗物系统重做）。
///
/// 规格：
///   - 角色向遗物（原版 83 件：角色向 73 + 充能球 7 + 召唤 2 + 烘焙手套，见 RelicScopeTable）
///     获得时弹头像二选一指定装备角色；仅剩一名角色存活时仍弹窗，死亡角色头像置灰不可选。
///   - 初始遗物（Rarity == Starter）不弹窗，自动绑定「获得方玩家」（随角色出生）。
///   - 其余遗物（牌向/能量/敌方/经济等）不弹窗，照旧全局。
///   - 仅双人模式生效；单玩家局所有行为零变化。
///
/// 效果重定向：
///   - 拾起型遗物（AfterObtained，如芒果/草莓的最大生命）：对每个需指定遗物类型的
///     AfterObtained override 加作用域补丁 → 期间 Player.get_Creature(主玩家) 重定向到装备角色。
///   - 战斗钩子型遗物（如铜质鳞片的荆棘）：Hook 派发器的 PlayerChoiceContext.PushModel/
///     PopModel 补丁建立遗物作用域 → 同上重定向。原版遗物钩子读 Owner.Creature（IL 实证：
///     BronzeScales AfterRoomEntered → PowerCmd.Apply(Owner.Creature ...)），
///     重定向后效果落在装备角色 creature 上。
///   - mod 遗物直接查 GetEquippedPlayer 结算（见 OgierGauntlet/OgierGloryMedal）。
///
/// 持久化：RunSavedData（键 ForeveDataKeys.RelicEquips，随局存档），槽位 0=主玩家 1=副玩家；
/// 读档时按 (Id, FloorAddedToDeck) 重建归属；查不到的需指定遗物按"主玩家"兜底。
/// </summary>
public static class DualCharacterRelicEquip
{
    private static readonly Logger Logger = RitsuLibFramework.CreateLogger("foreve_relic_equip");

    // ── 运行时装备归属（遗物实例 → 所装备玩家） ───────────────
    private static readonly Dictionary<RelicModel, Player> Equipped = new();

    // ── 遗物作用域栈（理论嵌套极少；thread-static 防异步串扰） ──
    [ThreadStatic] private static Stack<RelicModel>? _scopeStack;

    // ── RelicCmd.Obtain 拦截状态（参照 DualCharacterEventPatches 的 Prefix+Postfix 模式） ──
    private static bool _applyingChoice;
    private static bool _interceptedObtain;
    private static Task<RelicModel>? _pendingObtain;

    // ── Run 存档 ──
    private static RunSavedData<RelicEquipRunData>? _savedData;

    /// <summary>读档时 RunSavedData 尚未附加 → 标记延迟到首场战斗开始再重建（SilverKey 同款时序兜底）。</summary>
    private static bool _pendingLoadRestore;

    /// <summary>存档数据结构：每件需指定遗物一条 (Id, Floor, Slot)。</summary>
    public sealed class RelicEquipRunData
    {
        public List<RelicEquipEntry> Entries { get; set; } = new();
    }

    public sealed class RelicEquipEntry
    {
        public string Id { get; set; } = "";
        public int Floor { get; set; }
        public int Slot { get; set; } // 0=主玩家, 1=副玩家
    }

    private static bool _installed;

    public static void EnsureInstalled()
    {
        if (_installed) return;
        _installed = true;

        var harmony = new Harmony("foreve.relic_equip");

        // 1) RelicCmd.Obtain(relic, player, index)：获得时弹窗指定 + 初始遗物自动绑定
        var obtain = AccessTools.Method(typeof(RelicCmd), "Obtain",
            new[] { typeof(RelicModel), typeof(Player), typeof(int) });
        if (obtain != null)
        {
            harmony.Patch(obtain,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(DualCharacterRelicEquip), nameof(RelicCmdObtainPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(DualCharacterRelicEquip), nameof(RelicCmdObtainPostfix))));
        }

        // 2) 需指定遗物类型的 AfterObtained override：拾起效果执行期间建立遗物作用域
        //    （async 壳方法的 Prefix/Postfix 天然覆盖首个 await 之前的同步段 —— 目标解析就在其中）
        var afterObtainedBase = AccessTools.Method(typeof(RelicModel), nameof(RelicModel.AfterObtained));
        if (afterObtainedBase != null)
        {
            harmony.Patch(afterObtainedBase,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(DualCharacterRelicEquip), nameof(AfterObtainedPrefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(DualCharacterRelicEquip), nameof(AfterObtainedPostfix))));
        }
        var patchedTypes = 0;
        var patchedHookMethods = 0;
        foreach (var type in EnumerateRelicTypes())
        {
            if (!RelicScopeTable.IsTypeCharacterBound(type.FullName)) continue;
            var declared = type.GetMethod(nameof(RelicModel.AfterObtained),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (declared != null)
            {
                harmony.Patch(declared,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(DualCharacterRelicEquip), nameof(AfterObtainedPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(DualCharacterRelicEquip), nameof(AfterObtainedPostfix))));
                patchedTypes++;
            }

            // 无 choiceContext 的钩子（房间/战斗收尾/篝火/金币等派发不走 PlayerChoiceContext.PushModel，
            // IL 实证：Hook.AfterRoomEntered = IterateHookListeners → model.AfterRoomEntered(room)，无 PushModel）：
            // 逐类型给 override 加作用域。若某钩子恰好也走 ctx 派发（PushModel 已建作用域），
            // 栈式作用域二次压栈/弹栈对称，行为一致（双保险）。
            foreach (var hookName in NoContextHookNames)
            {
                var declaredHook = type.GetMethod(hookName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (declaredHook == null || declaredHook.IsSpecialName) continue;
                harmony.Patch(declaredHook,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(DualCharacterRelicEquip), nameof(RelicHookPrefix))),
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(DualCharacterRelicEquip), nameof(RelicHookPostfix))));
                patchedHookMethods++;
            }
        }

        // 3) 战斗钩子派发：PushModel/PopModel 建立/解除遗物作用域（Hook 类对每个 model 逐个 Push→调用→Pop）
        var push = AccessTools.Method(typeof(PlayerChoiceContext), "PushModel",
            new[] { typeof(AbstractModel) });
        var pop = AccessTools.Method(typeof(PlayerChoiceContext), "PopModel",
            new[] { typeof(AbstractModel) });
        if (push != null)
            harmony.Patch(push, prefix: new HarmonyMethod(AccessTools.Method(typeof(DualCharacterRelicEquip), nameof(PushModelPrefix))));
        if (pop != null)
            harmony.Patch(pop, prefix: new HarmonyMethod(AccessTools.Method(typeof(DualCharacterRelicEquip), nameof(PopModelPrefix))));

        // 4) 存读档：开局/读档重建（读档按存档条目恢复；查不到默认主玩家）
        // 注意勿与 DualCharacterCombatPatches 的 Player.get_Creature 重定向冲突：
        // 那里只在 SetupPlayerTurn/FlushPlayerHand 执行期间有条目（死亡主玩家保护），
        // 本模块重定向只在遗物作用域内生效，两者条件互斥。
        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(OnRunStarted, replayCurrentState: false);
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(OnRunLoaded, replayCurrentState: false);
        // 读档时序兜底：RunSavedData 有时在 RunLoadedEvent 时尚未附加（与银钥同因），
        // 首场战斗开始时补一次重建。
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(e =>
        {
            try
            {
                if (!_pendingLoadRestore || e.RunState.Players.Count != 2) return;
                if (e.RunState is not RunState rs) return;
                _pendingLoadRestore = false;
                RebuildFromSave(rs);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Foreve][Dual] 遗物装备战斗开始补重建异常: {ex.Message}");
            }
        }, replayCurrentState: false);
        var store = RunSavedDataStore.For(ForeveMod.ModId);
        _savedData = store.Register<RelicEquipRunData>(ForeveDataKeys.RelicEquips, () => new RelicEquipRunData());

        Logger.Info($"[Foreve][Dual] 遗物装备系统已装 (Obtain={obtain != null}, AfterObtainedBase={(afterObtainedBase != null)}, " +
                    $"AfterObtainedTypes={patchedTypes}, NoContextHookPatches={patchedHookMethods}, PushModel={push != null}, PopModel={pop != null})");
    }

    // ─────────────────────────────────────────────────────────────
    // 对外 API
    // ─────────────────────────────────────────────────────────────

    /// <summary>该遗物当前是否已记录装备角色（null = 未指定）。</summary>
    public static Player? GetEquippedPlayer(RelicModel relic)
        => relic != null && Equipped.TryGetValue(relic, out var player) ? player : null;

    /// <summary>记录装备归属（并写 Run 存档）。player 为 null 时清除记录。</summary>
    public static void SetEquipped(RelicModel relic, Player? player)
    {
        if (relic == null) return;
        if (player == null)
        {
            Equipped.Remove(relic);
            return;
        }
        Equipped[relic] = player;
        TryPersist(relic, player);
    }

    /// <summary>遗物作用域内的装备玩家：作用域为空或未指定时返回 null。</summary>
    public static Player? GetScopedEquippedPlayer()
    {
        var relic = CurrentScopedRelic;
        if (relic == null) return null;
        return GetEquippedPlayer(relic);
    }

    /// <summary>当前作用域遗物（栈顶），无则 null。</summary>
    public static RelicModel? CurrentScopedRelic
        => _scopeStack != null && _scopeStack.Count > 0 ? _scopeStack.Peek() : null;

    /// <summary>
    /// Player.get_Creature 重定向判定（由 DualCharacterCombatPatches 的
    /// PlayerCreatureOverridePrefix 在表未命中后调用）：
    /// 遗物作用域内 && 该遗物装备了「另一名」玩家 && 装备角色存活 && 请求者是主玩家
    /// → 返回装备角色 creature，让原版遗物效果（Owner.Creature）落在装备角色身上。
    /// </summary>
    public static bool TryGetScopedCreatureRedirect(Player player, out Creature result)
    {
        result = null!;
        if (player == null || !DualCharacterState.Enabled) return false;

        var scoped = CurrentScopedRelic;
        if (scoped == null) return false;

        var equipped = GetEquippedPlayer(scoped);
        if (equipped == null) return false;
        if (ReferenceEquals(equipped, player)) return false; // 装备者就是请求者：零变化

        // 只重定向「主玩家」的读取（原版遗物 owner 恒为主玩家；副玩家读取保持自身）
        if (!DualCharacterState.IsMainPlayer(player)) return false;

        var equippedCreature = equipped.Creature;
        if (equippedCreature == null || equippedCreature.IsDead) return false;
        result = equippedCreature;
        return true;
    }

    /// <summary>该遗物在双人模式下是否需要「获得时指定装备角色」。</summary>
    public static bool RequiresEquipCharacter(RelicModel relic)
        => DualCharacterState.Enabled
           && relic is { Id: not null }
           && RelicScopeTable.RequiresEquipCharacter(relic);

    /// <summary>
    /// 效果结算用装备者解析（mod 遗物调用）：双人模式按装备记录，未记录时初始遗物按
    /// RegisterCharacterStarterRelicAttribute 找回所属角色玩家（旧档/时序兜底），
    /// 仍找不到回退 owner；单玩家模式恒返回 owner（零变化）。
    /// </summary>
    public static Player ResolveEquippedPlayer(RelicModel relic, Player owner)
    {
        if (!DualCharacterState.Enabled) return owner;
        var equipped = GetEquippedPlayer(relic);
        if (equipped != null) return equipped;

        if (relic.Rarity == RelicRarity.Starter)
        {
            var byStarter = ResolveStarterEquipPlayer(
                relic, DualCharacterState.MainPlayer, DualCharacterState.SecondaryPlayer);
            if (byStarter != null) return byStarter;
        }

        return owner;
    }

    // ─────────────────────────────────────────────────────────────
    // 作用域管理
    // ─────────────────────────────────────────────────────────────

    public static void PushScope(RelicModel relic)
    {
        if (relic == null || !DualCharacterState.Enabled) return;
        _scopeStack ??= new Stack<RelicModel>();
        _scopeStack.Push(relic);
    }

    public static void PopScope()
    {
        if (_scopeStack == null || _scopeStack.Count == 0) return;
        _scopeStack.Pop();
        if (_scopeStack.Count == 0) _scopeStack = null;
    }

    // ─────────────────────────────────────────────────────────────
    // Harmony 补丁：RelicCmd.Obtain（获得时弹窗 / 初始遗物自动绑定）
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 拦截条件：双人模式 && 尚无归属记录。
    /// 初始遗物（Starter）不弹窗、无条件自动绑定「获得方玩家」——不经过需指定分类表
    /// （朵尔营养缸等角色初始遗物由此随角色出生正确绑定；分类表只约束非初始遗物是否弹窗）。
    /// 非初始且需指定 && 获得方是主玩家 → 记录待弹窗任务并跳过原方法（Postfix 注入任务）。
    /// 重入（选择后重新 Invoke 原方法）由「已有记录/未记录中」判定放行。
    /// </summary>
    private static bool RelicCmdObtainPrefix(object[] __args, MethodInfo __originalMethod)
    {
        try
        {
            if (_applyingChoice || _pendingObtain != null || __args == null || __args.Length < 3)
                return true;

            var relic = __args[0] as RelicModel;
            var player = __args[1] as Player;
            if (relic == null || player == null) return true;
            if (!DualCharacterState.Enabled) return true; // 单玩家/真联机零变化
            if (Equipped.ContainsKey(relic)) return true; // 已有归属（二次进入/读档恢复）

            if (relic.Rarity == RelicRarity.Starter)
            {
                // 初始遗物：随角色出生，无条件绑定获得方玩家，不弹窗。
                // ⚠️ 必须在 RequiresEquipCharacter 之前：mod 角色初始遗物不一定进分类表，
                // 否则副角色（如朵尔）的初始遗物不会记录归属，效果回退到主玩家。
                SetEquipped(relic, player);
                return true;
            }

            if (!RequiresEquipCharacter(relic)) return true;
            if (!DualCharacterState.IsMainPlayer(player)) return true; // 只处理主玩家获得路径

            _interceptedObtain = true;
            _pendingObtain = AskAndObtainAsync(relic, player, __args, __originalMethod);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Foreve][Dual] 遗物获得拦截异常（放行原流程）: {ex.Message}");
            return true;
        }
    }

    /// <summary>被拦截时把「选择+入账」任务注入 __result（原方法被跳过，__result 为 null）。</summary>
    private static void RelicCmdObtainPostfix(ref Task<RelicModel> __result)
    {
        if (!_interceptedObtain) return;
        _interceptedObtain = false;
        var pending = _pendingObtain;
        _pendingObtain = null;
        __result = pending ?? __result ?? Task.FromResult<RelicModel>(null!);
    }

    /// <summary>弹窗指定装备角色 → 记录归属 → 调原方法真正入账（效果随装备角色结算）。</summary>
    private static async Task<RelicModel> AskAndObtainAsync(
        RelicModel relic, Player obtainer, object[] args, MethodInfo originalMethod)
    {
        Player chosen = obtainer;
        try
        {
            var main = DualCharacterState.MainPlayer;
            var secondary = DualCharacterState.SecondaryPlayer;
            if (main?.Creature != null && secondary?.Creature != null)
            {
                var disabled = new List<Player>(2);
                if (main.Creature.IsDead) disabled.Add(main);
                if (secondary.Creature.IsDead) disabled.Add(secondary);

                var selected = await DualCharacterChoiceUi.ShowAsync(
                    "此遗物由谁装备？", new List<Player> { main, secondary }, disabled,
                    fallback: obtainer.Creature ?? main.Creature!);

                chosen = ReferenceEquals(selected, secondary.Creature) ? secondary : main;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Foreve][Dual] 遗物装备选择弹窗失败，默认归获得方: {ex.Message}");
        }

        // 记录归属（在 AfterObtained 之前 → 拾起效果按装备角色结算）
        SetEquipped(relic, chosen);

        _applyingChoice = true;
        try
        {
            var result = (Task<RelicModel>)originalMethod.Invoke(null, args)!;
            var obtained = await result;
            // 入账完成后再写一次存档：此刻 FloorAddedToDeck 才被原方法写入真实楼层
            // （原方法时序：AddRelicInternal → set_FloorAddedToDeck(TotalFloor) → AfterObtained），
            // 之前的 SetEquipped 存档用的是楼层 0，读档按楼层匹配会落空（2026-08-18 实测修复）。
            TryPersist(relic, chosen);
            return obtained;
        }
        finally
        {
            _applyingChoice = false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Harmony 补丁：AfterObtained 作用域（拾起型角色向遗物）
    // ─────────────────────────────────────────────────────────────

    /// <summary>无 choiceContext 派发、需要逐类型补作用域的钩子（IL 实证这些派发不经过 PushModel）。</summary>
    private static readonly string[] NoContextHookNames =
    {
        nameof(AbstractModel.BeforeCombatStart),
        nameof(AbstractModel.BeforeCombatStartLate),
        nameof(AbstractModel.BeforeRoomEntered),
        nameof(AbstractModel.AfterRoomEntered),
        nameof(AbstractModel.AfterCombatEnd),
        nameof(AbstractModel.AfterCombatVictory),
        nameof(AbstractModel.AfterCombatVictoryEarly),
        nameof(AbstractModel.AfterRestSiteHeal),
        nameof(AbstractModel.AfterRestSiteSmith),
        nameof(AbstractModel.AfterGoldGained),
    };

    private static void AfterObtainedPrefix(RelicModel __instance)
        => PushScope(__instance);

    private static void AfterObtainedPostfix(RelicModel __instance)
        => PopScope();

    /// <summary>无上下文钩子作用域前缀（入则压栈）。</summary>
    private static void RelicHookPrefix(object __instance)
    {
        if (__instance is RelicModel relic)
            PushScope(relic);
    }

    /// <summary>无上下文钩子作用域后缀（出则弹栈）。</summary>
    private static void RelicHookPostfix(object __instance)
    {
        if (__instance is RelicModel relic)
            PopScope();
    }

    // ─────────────────────────────────────────────────────────────
    // Harmony 补丁：战斗钩子派发作用域（Hook: PushModel → 调用 → PopModel）
    // ─────────────────────────────────────────────────────────────

    private static void PushModelPrefix(AbstractModel model)
    {
        if (model is RelicModel relic)
            PushScope(relic);
    }

    private static void PopModelPrefix(AbstractModel model)
    {
        if (model is RelicModel relic)
            PopScope();
    }

    // ─────────────────────────────────────────────────────────────
    // 存读档
    // ─────────────────────────────────────────────────────────────

    private static void OnRunStarted(RunStartedEvent e)
    {
        try
        {
            if (e.RunState.Players.Count != 2) return;
            // 初始遗物在开局 Obtain 时（RunStartedEvent 之前）已自动绑定（Rarity==Starter 分支）。
            // ⚠️ 不能在此 Clear：开局流程是「创建玩家 → 初始遗物 Obtain(记录归属) → RunStartedEvent」，
            // 清空会把刚写入的初始遗物归属全部丢掉。实例字典按引用键控，旧局实例不会与新局冲突，
            // 残留条目无害；读档路径才需要 Clear+重建（OnRunLoaded 已做）。
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Foreve][Dual] 遗物装备 RunStarted 处理异常: {ex.Message}");
        }
    }

    private static void OnRunLoaded(RunLoadedEvent e)
    {
        try
        {
            if (e.RunState.Players.Count != 2) return;
            Equipped.Clear();

            // 读档条目按 Id+Floor 重建；未被存档覆盖的需指定遗物 → 主玩家兜底
            var entries = new List<RelicEquipEntry>();
            if (_savedData == null || !_savedData.TryGet(e.RunState, out var data)
                || data.Entries == null || data.Entries.Count == 0)
            {
                _pendingLoadRestore = true;
            }
            else
            {
                entries = data.Entries;
            }

            RebuildFromSave(e.RunState, entries);
            Logger.Info($"[Foreve][Dual] 遗物装备读档恢复: 需指定遗物 {Equipped.Count} 件" +
                        (_pendingLoadRestore ? "（存档未就绪，首场战斗再补）" : ""));
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Foreve][Dual] 遗物装备读档恢复异常: {ex.Message}");
        }
    }

    /// <summary>按存档条目重建装备归属（entries 为空时按初始遗物规则/主玩家兜底）。</summary>
    private static void RebuildFromSave(RunState runState, List<RelicEquipEntry>? entries = null)
    {
        entries ??= new List<RelicEquipEntry>();
        if (_savedData != null && _savedData.TryGet(runState, out var data))
            entries = data.Entries ?? new List<RelicEquipEntry>();

        var main = runState.Players[0];
        var secondary = runState.Players[1];
        var relics = GetPlayerRelics(main);
        if (relics == null) return;

        foreach (var relic in relics)
        {
            if (!RelicScopeTable.RequiresEquipCharacter(relic)) continue;
            var keyId = relic.Id?.Entry ?? relic.GetType().Name;
            var floor = relic.FloorAddedToDeck;
            var entry = entries.FirstOrDefault(x => x.Id == keyId && x.Floor == floor);
            if (entry != null)
            {
                Equipped[relic] = entry.Slot == 1 && secondary != null ? secondary : main;
            }
            else
            {
                // 兜底 1：初始遗物按「所属角色的玩家」恢复（开局时若 RunState 未就绪会漏存）；
                // 兜底 2：其余需指定遗物默认主玩家。
                Equipped[relic] = ResolveStarterEquipPlayer(relic, main, secondary) ?? main;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 工具
    // ─────────────────────────────────────────────────────────────

    private static readonly FieldInfo RelicsField = AccessTools.Field(typeof(Player), "_relics");

    private static List<RelicModel>? GetPlayerRelics(Player player)
        => RelicsField?.GetValue(player) as List<RelicModel>;

    /// <summary>初始遗物（Rarity==Starter）按 RegisterCharacterStarterRelicAttribute 归属到对应角色的玩家；
    /// 非初始遗物/属性缺失返回 null（调用方回退主玩家）。</summary>
    private static Player? ResolveStarterEquipPlayer(RelicModel relic, Player? main, Player? secondary)
    {
        try
        {
            if (relic.Rarity != RelicRarity.Starter) return null;
            var attr = relic.GetType().GetCustomAttribute<RegisterCharacterStarterRelicAttribute>();
            if (attr?.CharacterType == null) return null;
            if (main?.Character != null && main.Character.GetType() == attr.CharacterType) return main;
            if (secondary?.Character != null && secondary.Character.GetType() == attr.CharacterType) return secondary;
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Foreve][Dual] 初始遗物归属解析失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>把 (Id, Floor, Slot) 写入 Run 存档；无法取到 RunState 时跳过（读档有兜底规则）。</summary>
    private static void TryPersist(RelicModel relic, Player player)
    {
        try
        {
            if (_savedData == null) return;
            var runState = player.RunState;
            if (runState is not RunState rs) return;

            var id = relic.Id?.Entry ?? relic.GetType().Name;
            var floor = relic.FloorAddedToDeck;
            // 槽位解析优先静态状态；初始遗物在 RunStartedEvent 之前 Obtain，那时
            // MainPlayer/SecondaryPlayer 尚未写入，用 RunState.Players 引用比较兜底
            //（否则副角色初始遗物会误存成槽位 0，读档后绑回主玩家）。
            var slot = DualCharacterState.IsSecondaryPlayer(player) ? 1 : 0;
            if (slot == 0 && rs.Players.Count > 1 && ReferenceEquals(player, rs.Players[1]))
                slot = 1;

            _savedData.Modify(rs, data =>
            {
                data.Entries ??= new List<RelicEquipEntry>();
                data.Entries.RemoveAll(x => x.Id == id && x.Floor == floor);
                data.Entries.Add(new RelicEquipEntry { Id = id, Floor = floor, Slot = slot });
            });
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Foreve][Dual] 遗物装备存档失败: {ex.Message}");
        }
    }

    /// <summary>枚举一切 RelicModel 子类（sts2.dll + mod 程序集）。</summary>
    private static IEnumerable<Type> EnumerateRelicTypes()
    {
        var seen = new HashSet<Type>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[]? types = null;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }
            catch
            {
                continue;
            }
            foreach (var t in types)
            {
                if (t == null || t.IsAbstract || !typeof(RelicModel).IsAssignableFrom(t)) continue;
                if (!seen.Add(t)) continue;
                yield return t;
            }
        }
    }
}