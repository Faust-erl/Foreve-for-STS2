using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using Foreve.Scripts.Content.Potions.Rotan;
using Foreve.Scripts.DualCharacter;
using STS2RitsuLib;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：战斗核心（批次 1b，2026-08-14 实测修复轮 v3 改造）。全部 IL 结论来自
/// C:\tmp\sts2_full.il + 运行时 sts2.dll 反编译（ilspycmd，2026-08-14 实证）。
///
/// ⚠️ 版本漂移教训：transpiler（IL 指令级匹配）在运行时可能静默匹配失败（OnPlayWrapper /
/// 旧 PerformMove d__103 均实测失效——攻击打全量=transpiler 未生效的典型表现）。
/// 本文件全部改为方法级 Prefix/Postfix（按名+签名解析，漂移安全）。
///
/// 覆盖 7 项：
///   1. LocalContext.GetMe 稳定返回主玩家 —— 双人模式下 NCombatUi/结束回合按钮/奖励归属
///      全部只绑主玩家（4 个重载 Prefix：ICombatState / IPlayerCollection /
///      IEnumerable&lt;Player&gt; / SerializableRun）。原逻辑本身按 NetId 查找（单机链 NetId=主玩家）
///      已正确，本 patch 是兜底，保证双玩家列表时确定性返回主玩家。
///   2. 副玩家自动 ready（回合不卡）—— AllPlayersReadyToEndTurn Prefix：双人模式只检查主玩家
///      （IL 1847617，原方法在 IsSingleplayerOrFakeMultiplayer=true 时恒 true；本 patch 覆盖
///      游戏被判定为多人时的场景）；SetReadyToEndTurn Prefix：副玩家调用直接跳过
///      （IL 1847361，public void SetReadyToEndTurn(Player, bool, Func&lt;Task&gt;)）。
///   3. 主玩家死亡保护 ——
///      a) CombatManager.HandlePlayerDeath Prefix return false（IL 1847861，public Task）：
///         双人模式不清空共享手牌/能量/星（原方法 RemoveFromCombat 全部牌 + SetEnergy(0) + SetStars(0)）；
///      b) PlayerCmd.EndTurn Prefix（IL 1836535，public static）：双人模式 && 玩家已死 &&
///         canBackOut==false && actionDuringEnemyTurn==null（= CreatureCmd.Kill d__14 IL 1831635
///         的死亡强制结束回合调用，与结束回合按钮 EndTurn(player,true,null)/虚空形态 EndTurn(owner,false,null)
///         的形态不同）→ 跳过。判负本身零 patch（Kill d__14 IL 1831573 Players.All(死亡) 原生）。
///   4. 敌人攻击/上 debuff 随机单目标（伤害×1/防御×2/辅助×0.8 加权）+ 意图 UI 显示目标角色 ——
///      a) 意图侧：NCreature.UpdateIntent Prefix（IL 960426，public Task）—— targets 参数
///         来自 RefreshIntents 的 Players.Select(Creature)（IL 966325），双人模式替换为
///         加权随机单目标并写入缓存（DualCharacterTargeting.GetOrRollTarget）；
///      b) 动作侧：MoveState.PerformMove 壳方法 Prefix（IL 1094219，public Task，
///         ref IEnumerable&lt;Creature&gt; targets——async 状态机 stfld 保存改后值，方法级漂移安全）：
///         双人模式按 AOE 判定（<see cref="DualCharacterTargeting.IsDebuffAoeMove(IReadOnlyList{AbstractIntent}, ICombatState)"/>
///         + moveState.Intents + 房间类型）决定两名全中或现掷单目标（RollTargetForCombat）；
///      c) 攻击意图目标小头像（2026-08-15 用户需求重新实现）：NCreature.UpdateIntent
///         Postfix 在每个 AttackIntent 的 %IntentHolder 右侧挂 28×28 角色头像
///         （TextureRect ExpandMode=IgnoreSize 强制缩放；旧版未设置 ExpandMode 导致
///         右下角巨大图片，已修复）；AOE/非攻击意图隐藏。
///      ⚠️ 批次 2b-2 增补：精英/Boss 战敌人 debuff 招式（意图含 Debuff/CardDebuff/Status 且不含
///      攻击/防御/治疗/增益/眩晕/召唤/逃脱）在双人模式下对两名角色全中（AOE）—— 意图侧与动作侧
///      共用同一 AOE 核心判定；本 Postfix 对 &gt;1 个玩家目标不显示单目标名（AOE 无单一目标可标）。
///   5. 战斗奖励/宝箱只出 1 份 —— 奖励生成按玩家循环（CombatRoom.OfferRoomEndRewards d__49
///      IL 195952 遍历 combatState.Players → RewardsCmd.GenerateForRoomEnd(player, room) IL 196004；
///      TreasureRoom.DoExtraRewardsIfNeeded d__10 IL 199250 遍历 _runState.Players）：
///      transpiler 把循环源 get_Players() 替换为 GetRewardPlayers(...)，只返回 [主玩家]。
///      普通宝箱走 OneOffSynchronizer.DoLocalTreasureRoomRewards → DoTreasureRoomRewards(LocalPlayer)
///      （IL 1075722，只有本地玩家一份），无需 patch。
///   6. 主玩家死亡仍可进入回合 —— SetupPlayerTurn d__104 MoveNext（IL 1853399）开头
///      玩家死亡直接跳过（IL_0029-0038）。双人模式共享手牌/能量都在主玩家 PCS，主玩家死亡
///      跳过 = 无回合可打。2026-08-15 新增 transpiler：把该死亡判定替换为
///      ShouldSkipDeadPlayerTurnSetup —— 双人模式主玩家死亡不跳过；副玩家/单玩家保持原样。
///      CombatState/PlayerCombatState 为 null 仅 Warn 并返回（IL_003f-00ad）；
///      MaxEnergy=0 → ResetEnergy 归零无害；空牌库 → 抽牌循环 0 张 + Draw 返回空
///      （handDraw 有 Max/Min 钳制 IL_038d-03c0）。
///      配套：DualCharacterCardOwnerPatch 在归属角色死亡时把卡牌效果重定向到存活角色。
///      ⚠️ 2026-08-16 增补（方法级兜底）：SetupPlayerTurn / FlushPlayerHand 壳方法执行期间，
///      死亡主玩家的 Player.get_Creature 临时重定向到存活副玩家 creature —— 原版 IsDead
///      检查看到“存活”，Setup 正常重置能量+抽牌，Flush 正常弃牌+EndOfTurnCleanup。
///      这同时修复“主角色死亡后进入回合不抽牌”与“手牌不清空导致后续回合抽不了牌”。
///   7. MultiplayerScalingModel 不开缩放确认 —— 缩放点是内容单例 MultiplayerScalingModel
///      （IL 1142310，CreateShared IL 172380 从 ModelDb.Singleton 取，非玩家数构造）的消费方：
///      a) Creature.ScaleHpForMultiplayer（IL 1767185，static，怪物 HP ×playerCount×perAct%）——
///         Prefix 双人模式返回原 HP；
///      b) MultiplayerScalingModel.ModifyBlockMultiplicative（IL 1142358，敌人格挡 hook）——
///         Prefix 双人模式返回 1；
///      c) PowerCmd.&lt;Apply&gt;d__2.MoveNext IL 1839408 调 PowerModel.GetScaledAmountForMultiplayer
///         （怪物 power 数值 ×playerCount×perAct%）—— transpiler 替换为
///         GetScaledAmountForMultiplayerSafe（双人模式返回未缩放值）。
///
/// ⚠️ 实测修复轮 v3（2026-08-14，本文件改造）：
///   - 敌人攻击打全量 → 移除失效的 &lt;PerformMove&gt;d__103 transpiler，改 MoveState.PerformMove
///     壳方法 Prefix（ref targets 单目标化；AOE 判定用 moveState.get_Intents() + 房间类型）。
///   - 意图目标名不显示 → NIntent.UpdateIntent Postfix 的 DualTargetLabel 增强（ZIndex 置顶 +
///     描边 + 简化判定，双人模式单目标时显示角色名；AOE 不显示）。
///   - Kill NRE（角色死亡崩溃，实测栈 CreatureCmd.&lt;KillWithoutCheckingWinCondition&gt;d__15.MoveNext
///     无子调用帧；上轮 d__15 MoveNext Finalizer 探针未触发=async 状态机内部 catch 吞异常，Finalizer
///     永远看不到）→ 改壳方法 KillWithoutCheckingWinCondition(Creature,bool,int) Prefix 探针
///     （打印 creature/玩家/战斗状态，定位 NRE 来源）+ creature==null 防御（跳过 Kill）。
///   - 角色死亡卡死（实测：Kill NRE → Kill 任务 fault → Damage → AttackCommand → 怪物招式 →
///     MoveState.PerformMove → TakeTurn → ExecuteEnemyTurn → StartTurn 整条敌人回合管道中断，
///     战斗停在敌人侧=卡死）→ 双人模式 Kill 任务 Postfix 兜底（OnlyOnFaulted 观察吞异常，
///     敌人回合管道不再被 Kill 异常打断；Kill NRE 修复后正常流程不受影响）。
///
/// ⚠️ 实测修复轮 v4（2026-08-14，本文件增补）：
///   - 副玩家回合开始 Draw 的 Shuffle NRE（CardPileCmd.&lt;Add&gt;d__9 ← Shuffle ←
///     ShuffleIfNecessary ← Draw ← SetupPlayerTurn d__104 +「没有足够的牌抽取」气泡）：
///     副玩家牌库/弃牌堆全空仍走 Draw(5) → 新增 CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot
///     Prefix（双人副玩家 return false，不可抽：无气泡+循环跳过）+ CardPileCmd.Draw 壳方法
///     Prefix（副玩家 count=0，双保险）。单玩家/主玩家零变化。
///
/// ⚠️ 2026-08-23 增补（主角色死亡后卡牌/遗物抽牌与药水不可用）：
///   - Draw 壳方法执行期间新增死亡主玩家 creature 重定向 scope（DrawDeadMainCreatureScopes 计数），
///     CardPileCmd.Add 的 card.Owner.Creature.IsDead 守卫因此放行主牌堆的牌。
///   - Player.DeactivateHooks Prefix：主玩家死亡但副玩家存活时不关闭主玩家模型 hooks，
///     遗物/药水/手牌等 hook 监听保持可触发。
///   - NPotionPopup._Ready / RefreshButtons / PotionModel.OnUseWrapper 临时重定向，
///     药水按钮按副玩家存活正常启用，Self 药水目标与 VFX 源落到存活副角色。
///
/// ⚠️ optimizer 分支（性能优化第 1 项）：全部调试探针已移除——
///   Kill 链探针（KillChainProbe*/ModelAfterDeathProbe*）、Kill 状态机 MoveNext Finalizer、
///   KillWithoutCheckingWinCondition 无条件 [KillProbe] 打印、get_Creature/get_OrbQueue/
///   get_Instance 热路径挂载探针。保留全部功能行为（null 防御、Task 吞异常兜底），
///   仅删除"定位用"日志与挂载点。
///
/// ⚠️ 接线（主流程在 Entry.cs 统一处理，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterCombatPatches.Install(Logger);
/// </summary>
public static class DualCharacterCombatPatches
{
    private static MegaCrit.Sts2.Core.Logging.Logger? _logger;

    /// <summary>
    /// 死亡主玩家 creature 临时重定向表（2026-08-16「主角色死亡后无法抽牌」修复）：
    /// SetupPlayerTurn / FlushPlayerHand 执行期间，把死亡主玩家的 get_Creature 映射到存活副玩家
    /// creature —— 原版这两个 async 方法开头的 IsDead 判定因此看到“存活”，正常走完
    /// 重置能量/抽牌/弃牌/EndOfTurnCleanup 全流程。任务完成后由包装 Task 的 finally 移除。
    /// 只用 TryAdd：同一玩家已有重定向时不会覆盖，避免并发 Setup/Flush 相互拆台。
    /// </summary>
    private static readonly ConcurrentDictionary<Player, Creature> DeadMainCreatureOverrides = new();

    /// <summary>
    /// 抽牌效果执行期间的死亡主玩家 creature 重定向深度（2026-08-23「主角色死亡后抽牌不结算」修复）。
    /// CardPileCmd.Draw 内部 CardPileCmd.Add 有 card.Owner.Creature.IsDead → success=false 的守卫；
    /// 主玩家死亡但副玩家存活时，主玩家牌堆里的牌仍应能被抽进共享手牌，因此 Draw 执行期间把主玩家
    /// get_Creature 重定向到存活副玩家。用计数而非单个条目：AfterCardDrawn 等钩子里可能嵌套抽牌，
    /// 只有最外层 Draw 结束才真正解除重定向。
    /// </summary>
    private static readonly ConcurrentDictionary<Player, int> DrawDeadMainCreatureScopes = new();

    /// <summary>药水死亡可用性 patch（NPotionPopup._Ready/RefreshButtons + PotionModel.OnUseWrapper）是否完整挂上（仅日志用）。</summary>
    private static bool _potionPopupPatchesInstalled;

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_combat");

        InstallGetMePatches(harmony);
        InstallReadyPatches(harmony);
        InstallDrawPatches(harmony);
        InstallDeathProtectionPatches(harmony);
        InstallTargetingPatches(harmony);
        InstallScanPatches(harmony); // 实测修复轮 v5：GetMethods 扫描 + __args 通用拦截（精确签名匹配在版本漂移下不可靠）
        DualCharacterCardOwnerPatch.Install(logger); // 批次 2b-2：卡牌对己效果归属（Entry.cs 已接线本 Install，无需改 Entry.cs）
        InstallRewardPatches(harmony);
        InstallScalingPatches(harmony);
        DualCharacterCombatUiPatch.Install(logger); // 副玩家 creature 血条/格挡常显（2026-08-14，IL 实证见该文件头注释）

        // 目标缓存清理：玩家回合开始（敌人回合刚结束）/战斗结束/战斗开始
        RitsuLibFramework.SubscribeLifecycle<SideTurnStartingEvent>(
            e => { if (e.Side == CombatSide.Player) DualCharacterTargeting.ClearTargetCache(); },
            replayCurrentState: false);
        RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(
            _ => DualCharacterTargeting.ClearTargetCache(), replayCurrentState: false);
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(
            _ =>
            {
                DualCharacterTargeting.ClearTargetCache();
                // 死亡保持到下一场战斗时，主玩家的 IsActiveForHooks 仍可能是 false：
                // 副玩家存活则恢复主玩家 hooks（遗物/卡牌效果继续可用，血量仍是 0）。
                ActivateMainHooksIfSecondaryAlive();
            }, replayCurrentState: false);

        logger.Info("[Foreve][Dual] 双角色战斗核心 patch 已安装 (GetMe主玩家/自动ready/副玩家不抽牌防ShuffleNRE/死亡保护/随机单目标+意图标记+精英Boss debuff AOE/卡牌归属/奖励1份/关闭多人缩放/Kill null防御+卡死兜底)");
    }

    // ── 1) LocalContext.GetMe 双人模式返回主玩家 ────────────────────────────

    private static void InstallGetMePatches(Harmony harmony)
    {
        var combat = AccessTools.Method(typeof(LocalContext), "GetMe", new[] { typeof(ICombatState) });
        var players = AccessTools.Method(typeof(LocalContext), "GetMe", new[] { typeof(IEnumerable<Player>) });
        var collection = AccessTools.Method(typeof(LocalContext), "GetMe", new[] { typeof(IPlayerCollection) });
        var serializable = AccessTools.Method(typeof(LocalContext), "GetMe", new[] { typeof(SerializableRun) });

        harmony.Patch(combat, prefix: new HarmonyMethod(GetMethod(nameof(GetMeCombatPrefix))));
        harmony.Patch(players, prefix: new HarmonyMethod(GetMethod(nameof(GetMePlayersPrefix))));
        harmony.Patch(collection, prefix: new HarmonyMethod(GetMethod(nameof(GetMeCollectionPrefix))));
        harmony.Patch(serializable, prefix: new HarmonyMethod(GetMethod(nameof(GetMeSerializablePrefix))));

        _logger?.Info($"[Foreve][Dual] LocalContext.GetMe 4 重载已 patch (combat={combat != null}, players={players != null}, collection={collection != null}, serializable={serializable != null})");
    }

    /// <summary>GetMe(ICombatState)：双玩家战斗时确定返回主玩家。</summary>
    private static bool GetMeCombatPrefix(ICombatState combatState, ref Player __result)
    {
        if (!DualCharacterState.Enabled || combatState?.Players == null || combatState.Players.Count < 2) return true;
        var main = DualCharacterState.MainPlayer;
        if (main == null || !ContainsPlayer(combatState.Players, main)) return true;
        __result = main;
        return false;
    }

    /// <summary>GetMe(IEnumerable&lt;Player&gt;)：列表含主玩家时返回主玩家（不含则放行原逻辑）。</summary>
    private static bool GetMePlayersPrefix(IEnumerable<Player> players, ref Player __result)
    {
        if (!DualCharacterState.Enabled || players == null) return true;
        var main = DualCharacterState.MainPlayer;
        if (main == null || !ContainsPlayer(players, main)) return true;
        __result = main;
        return false;
    }

    /// <summary>GetMe(IPlayerCollection)：同上。</summary>
    private static bool GetMeCollectionPrefix(IPlayerCollection playerCollection, ref Player __result)
    {
        if (!DualCharacterState.Enabled || playerCollection?.Players == null) return true;
        var main = DualCharacterState.MainPlayer;
        if (main == null || !ContainsPlayer(playerCollection.Players, main)) return true;
        __result = main;
        return false;
    }

    /// <summary>GetMe(SerializableRun)：存档读写时双人局归属主玩家（按 NetId 匹配）。</summary>
    private static bool GetMeSerializablePrefix(SerializableRun run, ref SerializablePlayer __result)
    {
        if (!DualCharacterState.Enabled || run?.Players == null || run.Players.Count < 2) return true;
        var main = DualCharacterState.MainPlayer;
        if (main == null) return true;
        foreach (var sp in run.Players)
        {
            if (sp.NetId == main.NetId) { __result = sp; return false; }
        }
        return true;
    }

    // ── 2) 回合 ready（副玩家自动 ready，回合不卡） ─────────────────────────

    private static void InstallReadyPatches(Harmony harmony)
    {
        var allReady = AccessTools.Method(typeof(CombatManager), "AllPlayersReadyToEndTurn");
        var setReady = AccessTools.Method(typeof(CombatManager), "SetReadyToEndTurn",
            new[] { typeof(Player), typeof(bool), typeof(Func<Task>) });
        harmony.Patch(allReady, prefix: new HarmonyMethod(GetMethod(nameof(AllPlayersReadyToEndTurnPrefix))));
        harmony.Patch(setReady, prefix: new HarmonyMethod(GetMethod(nameof(SetReadyToEndTurnPrefix))));
        _logger?.Info($"[Foreve][Dual] 回合 ready patch 已装 (AllPlayersReadyToEndTurn={allReady != null}, SetReadyToEndTurn={setReady != null})");
    }

    /// <summary>双人模式：全员 ready 判定只看主玩家（副玩家无 UI，永不主动 ready）。</summary>
    private static bool AllPlayersReadyToEndTurnPrefix(CombatManager __instance, ref bool __result)
    {
        if (!DualCharacterState.Enabled) return true;
        var main = DualCharacterState.MainPlayer;
        __result = main == null || __instance.IsPlayerReadyToEndTurn(main);
        return false;
    }

    /// <summary>
    /// 双人模式：
    ///   - 副玩家的 SetReadyToEndTurn 直接跳过（副玩家无 UI，永不主动 ready）。
    ///   - 死亡玩家在 StartTurn 收尾的自动 ready（canBackOut=false, action=null）也跳过 ——
    ///     否则主玩家死亡时原版会立即把主玩家标记 ready → 玩家回合被自动结束 → 连续敌人回合
    ///     （日志：Setting player 1 to ready at start of turn. IsDead: True. IsStartingTurn: True）。
    /// </summary>
    private static bool SetReadyToEndTurnPrefix(Player player, bool canBackOut, Func<Task> actionDuringEnemyTurn)
    {
        if (!DualCharacterState.Enabled) return true;
        if (DualCharacterState.IsSecondaryPlayer(player)) return false;
        if (player != null && player.Creature != null && player.Creature.IsDead
            && !canBackOut && actionDuringEnemyTurn == null)
            return false;
        return true;
    }

    // ── 2.5) 副玩家抽牌链修复（实测轮 2026-08-14：副玩家回合开始 Draw 的 Shuffle NRE） ──
    //
    // 症状：进入战斗后排角色（副玩家）显示「没有足够的牌抽取」气泡 + NRE：
    //   CardPileCmd.<Add>d__9.MoveNext ← Shuffle(choiceContext, player) ← ShuffleIfNecessary
    //   ← Draw ← CombatManager.<SetupPlayerTurn>d__104
    // 根因：合并后副玩家 Deck/DrawPile/DiscardPile 全空（牌全并入主玩家），但副玩家回合
    // 开始仍走 SetupPlayerTurn → Draw(5) → CheckIfDrawIsPossible...(空牌库→false 显示气泡)
    // → ShuffleIfNecessary → Shuffle → Add → NRE。
    // 修复（双保险，均按名+签名解析，漂移安全）：
    //   a) CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot(Player) Prefix —— 双人模式副玩家
    //      return false（不可抽：不显示气泡；Draw 状态机跳过抽牌循环 → Shuffle 不触发）。
    //      ⚠️ RitsuLib MaxHandSize 对该方法有 transpiler（PlayerArg0Transpiler）——Prefix
    //      与 transpiler 共存无冲突（Harmony 多 patch 正常）；Priority.First 优先执行。
    //   b) CardPileCmd.Draw(PlayerChoiceContext, decimal, Player, bool) 壳方法 Prefix ——
    //      双人模式副玩家 count 置 0（drawsRequested=Ceiling(0)=0 → 循环 0 次 → Shuffle
    //      不触发）。壳方法参数替换有效：async 状态机在 builder.Start 前 stfld 保存改后值。
    // 单玩家/主玩家：两处均放行（零变化）。

    private static void InstallDrawPatches(Harmony harmony)
    {
        var checkDraw = AccessTools.Method(typeof(CardPileCmd),
            "CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot", new[] { typeof(Player) });
        harmony.Patch(checkDraw,
            prefix: new HarmonyMethod(GetMethod(nameof(CheckIfDrawIsPossibleAndShowThoughtBubbleIfNotPrefix))));

        var draw = AccessTools.Method(typeof(CardPileCmd), "Draw",
            new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) });
        if (draw == null)
        {
            // 兜底：按名解析后校验参数个数（避免误中 Draw(PlayerChoiceContext, Player) 2 参重载）
            var anyDraw = AccessTools.Method(typeof(CardPileCmd), "Draw");
            if (anyDraw?.GetParameters().Length == 4) draw = anyDraw;
        }
        if (draw != null)
        {
            harmony.Patch(draw,
                prefix: new HarmonyMethod(GetMethod(nameof(CardPileCmdDrawPrefix))),
                postfix: new HarmonyMethod(GetMethod(nameof(CardPileCmdDrawPostfix))));
        }

        _logger?.Info($"[Foreve][Dual] 副玩家抽牌修复 patch 已装 (CheckIfDrawIsPossible={checkDraw != null}, Draw={draw != null})");
    }

    /// <summary>
    /// 实测修复轮 v5：GetMethods 扫描 + __args 通用拦截。
    /// ⚠️ 版本漂移教训（2026-08-14 实证）：精确签名 AccessTools.Method 匹配的 patch
    /// （NCreature.UpdateIntent / MoveState.PerformMove / KillWithoutCheckingWinCondition）
    /// 挂载日志全 True 但运行时全部未执行（KillProbe 无条件探针零输出）——精确签名在
    /// 运行时方法签名漂移时不可靠。改用：按方法名扫描 + object[] __args 通用参数拦截
    /// （EventPatches 的 DamagePrefix 同款模式，实测可靠）。
    /// 覆盖：
    ///   1. CreatureCmd.Damage 多目标重载（参数含 IEnumerable&lt;Creature&gt;）——敌人攻击收敛单目标
    ///      （用户点破：原版多人模式只有群体攻击，单目标逻辑需 mod 补充；Damage 是伤害必经点，
    ///      无论上游传什么，这里收敛）
    ///   2. CreatureCmd.Kill(Creature, bool) 壳方法——null 防御（弃局 GuaranteeKillAllPlayers
    ///      杀 player.Creature，副玩家 creature 可能为 null → Kill(null) NRE，2026-08-14 实测）
    ///   3. LocalContext.IsMe(Player/Creature)——双人模式主玩家恒 true（主玩家血条被判定为
    ///      「远程玩家」隐藏的根因：_isRemotePlayerOrPet = IsPlayer &amp;&amp; !IsMe）
    /// </summary>
    private static void InstallScanPatches(Harmony harmony)
    {
        var flags = BindingFlags.Public | BindingFlags.Static;
        var damageCount = 0;
        foreach (var m in typeof(CreatureCmd).GetMethods(flags))
        {
            if (m.Name != "Damage") continue;
            if (!m.GetParameters().Any(p => p.ParameterType == typeof(IEnumerable<Creature>))) continue;
            harmony.Patch(m, prefix: new HarmonyMethod(GetMethod(nameof(DamageMultiPrefix))));
            damageCount++;
        }

        // Kill 2 参壳方法（实测轮 8 复测）：覆盖 (Creature, bool) 与
        // (IReadOnlyCollection<Creature>, bool) 两个重载（d__14 集合版）。
        // 背景：3 参 KillWithoutCheckingWinCondition 壳被 JIT 内联（Harmony detour 被绕过，
        // Prefix 零输出实证）→ 原来挂它身上的 Task 吞异常兜底从未生效 → 弃局/死亡 NRE
        // 中断敌人回合管道 → 卡死。2 参 Kill detour 有效（KillShellPrefix 日志实证）→
        // 兜底（KillShellSafePostfix）改挂这里。
        // ⚠️ 集合版只挂 Postfix 不挂 Prefix：KillShellPrefix 的 __args[0] as Creature 会把
        // 集合当 null 误判「跳过 Kill」（null 防御逻辑只针对单 creature 版）。
        var killCount = 0;
        var killSafeCount = 0;
        foreach (var m in typeof(CreatureCmd).GetMethods(flags))
        {
            if (m.Name != "Kill") continue;
            var ps = m.GetParameters();
            if (ps.Length != 2) continue;
            var isCreature = ps[0].ParameterType == typeof(Creature);
            var isCollection = ps[0].ParameterType == typeof(IReadOnlyCollection<Creature>);
            if (!isCreature && !isCollection) continue;
            harmony.Patch(m,
                prefix: isCreature ? new HarmonyMethod(GetMethod(nameof(KillShellPrefix))) : null,
                postfix: new HarmonyMethod(GetMethod(nameof(KillShellSafePostfix))));
            killCount++;
            killSafeCount++;
        }

        var isMeCount = 0;
        foreach (var m in typeof(LocalContext).GetMethods(flags))
        {
            if (m.Name != "IsMe") continue;
            var ps = m.GetParameters();
            if (ps.Length != 1 || (ps[0].ParameterType != typeof(Player) && ps[0].ParameterType != typeof(Creature))) continue;
            harmony.Patch(m, prefix: new HarmonyMethod(GetMethod(nameof(IsMePrefix))));
            isMeCount++;
        }

        // 意图显示：NCreature.UpdateIntent 扫描 Prefix（掷目标写缓存+targets 替换单目标）——
        // 精确签名版（NCreatureUpdateIntentPrefix）实测未执行（版本漂移），扫描式可靠
        var intentCount = 0;
        foreach (var m in typeof(NCreature).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (m.Name != "UpdateIntent") continue;
            var ps = m.GetParameters();
            if (ps.Length != 1 || ps[0].ParameterType != typeof(IEnumerable<Creature>)) continue;
            harmony.Patch(m,
                prefix: new HarmonyMethod(GetMethod(nameof(NCreatureUpdateIntentScanPrefix))),
                postfix: new HarmonyMethod(GetMethod(nameof(NCreatureUpdateIntentScanPostfix))));
            intentCount++;
        }


        // Kill 壳方法 KillWithoutCheckingWinCondition 扫描式重挂（实测修复轮 7 + 轮 8）：
        // 轮 6 的精确签名 AccessTools.Method 挂载实测零输出（KillProbe 无条件探针没打印）
        // = 版本漂移 → Task 吞异常兜底从未生效 → 死亡 NRE 中断敌人回合管道 → 卡死。
        // GetMethods 按名+参数扫描（同 KillShellPrefix 模式），确保 Prefix 探针 + Postfix
        // Task 兜底真正挂上（当前版本仅一个 (Creature, bool, int) 重载，IL 实证）。
        // ⚠️ 轮 8 修正：KillWithoutCheckingWinCondition 是 private static（反编译实证）——
        // 上面的公共 flags（Public|Static）扫不到它 → KillNoCheck=0 → 兜底从未挂上。
        // 单独建含 NonPublic 的 flags 只用于 Kill 壳扫描，避免其他扫描误中 private 方法。
        var killNoCheckFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var killNoCheckCount = 0;
        foreach (var m in typeof(CreatureCmd).GetMethods(killNoCheckFlags))
        {
            if (m.Name != "KillWithoutCheckingWinCondition") continue;
            var ps = m.GetParameters();
            if (ps.Length != 3 || ps[0].ParameterType != typeof(Creature)) continue;
            harmony.Patch(m,
                prefix: new HarmonyMethod(GetMethod(nameof(KillWithoutCheckingWinConditionPrefix))),
                postfix: new HarmonyMethod(GetMethod(nameof(KillWithoutCheckingWinConditionPostfix))));
            killNoCheckCount++;
        }

        _logger?.Info($"[Foreve][Dual] 扫描式 patch 已装 (Damage多目标={damageCount}, Kill壳={killCount}, Kill壳兜底={killSafeCount}, KillNoCheck={killNoCheckCount}, IsMe={isMeCount}, 意图显示={intentCount})");
    }

    /// <summary>
    /// 敌人攻击收敛单目标（Damage 必经点）：双人模式 &amp;&amp; dealer 是怪物 &amp;&amp; 非精英/Boss debuff AOE
    /// → targets 收敛为加权随机单目标（已死亡玩家不参与）。玩家卡牌/药水 AOE 不干预（dealer 非怪物）。
    /// </summary>
    [HarmonyPriority(Priority.First)]
    private static bool DamageMultiPrefix(object[] __args)
    {
        try
        {
            if (!DualCharacterState.Enabled || __args == null || __args.Length < 5) return true;
            if (__args[1] is not IEnumerable<Creature> targets) return true;
            var dealer = __args[4] as Creature;
            if (dealer?.Monster == null) return true; // 只有怪物攻击收敛；玩家 AOE 保持

            ICombatState? cs = null;
            var playerCreatures = new List<Creature>(2);
            foreach (var c in targets)
            {
                if (c == null) continue;
                if (cs == null && c.CombatState != null) cs = c.CombatState;
                if (c.IsPlayer) playerCreatures.Add(c);
            }
            if (cs == null || !DualCharacterState.IsDualMode(cs)) return true;
            if (playerCreatures.Count < 2) return true;

            // 精英/Boss debuff AOE：两名全中（不收敛）
            if (DualCharacterTargeting.IsDebuffAoeMove(dealer.Monster, cs)) return true;

            // 优先读意图显示时掷好的缓存目标（意图显示 == 实际命中），缺失则现掷
            var target = DualCharacterTargeting.GetCachedTarget(dealer.Monster, cs)
                         ?? DualCharacterTargeting.RollTargetForCombat(cs);
            if (target != null)
            {
                __args[1] = new[] { target };
            }
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] Damage 收敛异常: {e}");
        }
        return true;
    }

    /// <summary>Kill 壳方法 null 防御（弃局 GuaranteeKillAllPlayers 杀 null creature 时跳过，防 NRE 卡死）。</summary>
    [HarmonyPriority(Priority.First)]
    private static bool KillShellPrefix(object[] __args)
    {
        try
        {
            if (__args == null || __args.Length < 1) return true;
            if (__args[0] as Creature == null) return false; // null 防御：跳过 Kill
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] Kill 壳防御异常: {e}");
        }
        return true;
    }

    /// <summary>
    /// Kill 2 参壳 Postfix 吞异常兜底（实测轮 8 复测）：3 参 KillWithoutCheckingWinCondition
    /// 壳方法被 JIT 内联（Harmony detour 被绕过，Prefix 零输出实证）→ 原来挂它身上的
    /// Task 兜底从未生效。2 参 Kill(Creature, bool) 的 detour 有效（KillShellPrefix 日志实证）
    /// → 兜底改挂这里：把 Kill 返回的 Task 替换为吞异常包装（同步已 fault → CompletedTask；
    /// 异步 → await 吞掉），保证弃局/死亡流程的 NRE 不沿 await 链传播中断敌人回合管道（卡死）。
    /// </summary>
    private static void KillShellSafePostfix(ref Task __result)
    {
        try
        {
            if (!DualCharacterState.Enabled || __result == null) return;
            var original = __result;
            if (original.IsFaulted)
            {
                GD.Print($"[Foreve][Dual] Kill 兜底(同步异常): {original.Exception?.InnerException?.Message}");
                __result = Task.CompletedTask;
                return;
            }
            __result = WrapKillTask(original);
        }
        catch (Exception e) { GD.Print($"[Foreve][Dual] Kill 兜底异常: {e}"); }
    }

    /// <summary>
    /// IsMe 重载 Prefix：双人模式主玩家恒 true；副玩家在「副玩家侧卡牌效果结算中」也视为本地。
    /// 根因：NCreature._Ready 的 _isRemotePlayerOrPet = IsPlayer &amp;&amp; !IsMe(entity)—— 
    /// 主玩家血条初始隐藏（悬停才显示）= IsMe(主玩家)=false 的远程判定（2026-08-14 实测）。
    /// 2026-08-24：副玩家自有生成卡（如指定副角色时创建的小刀）入主手牌时，原版按
    /// IsMe(副玩家)=false 走远程分支，不创建手牌视觉节点 → 数据在手牌但看不见/像没获得。
    /// </summary>
    [HarmonyPriority(Priority.First)]
    private static bool IsMePrefix(object[] __args, ref bool __result)
    {
        try
        {
            if (!DualCharacterState.Enabled || __args == null || __args.Length < 1) return true;
            var main = DualCharacterState.MainPlayer;
            if (main == null) return true;

            Player? player = __args[0] as Player;
            if (player == null && __args[0] is Creature c && c.Player != null) player = c.Player;
            if (player == null) return true;

            if (ReferenceEquals(player, main))
            {
                __result = true;
                return false;
            }

            // 副玩家卡牌结算 scope 内：副玩家生成卡的入堆/出牌视觉也要走本地路径。
            if (DualCharacterCardOwnerPatch.IsSecondaryCardEffectActive
                && DualCharacterState.IsSecondaryPlayer(player))
            {
                __result = true;
                return false;
            }

            __result = false;
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 意图显示目标（扫描式）：NCreature.UpdateIntent(IEnumerable&lt;Creature&gt; targets) 实例方法 Prefix——
    /// 双人模式怪物意图刷新时掷加权随机单目标写缓存（key=怪物 creature），targets 替换为 [目标]。
    /// Damage 收敛（DamageMultiPrefix）读同一缓存 → 意图显示 == 实际命中。
    /// AOE（精英/Boss debuff）不替换（两名全中，头像不显示）。
    /// </summary>
    [HarmonyPriority(Priority.First)]
    private static bool NCreatureUpdateIntentScanPrefix(NCreature __instance, object[] __args)
    {
        try
        {
            if (!DualCharacterState.Enabled || __args == null || __args.Length < 1) return true;
            if (__args[0] is not IEnumerable<Creature> targets) return true;
            var entity = __instance?.Entity;
            if (entity == null || entity.Monster == null) return true;

            var list = targets as IReadOnlyList<Creature> ?? targets.ToList();
            if (list.Count != 2 || list[0] == null || list[1] == null
                || !list[0].IsPlayer || !list[1].IsPlayer) return true;

            var combatState = entity.CombatState;
            if (combatState == null || !DualCharacterState.IsDualMode(combatState)) return true;

            // 洗入牌库类意图（往手牌/卡组/弃牌堆加牌，轮 8）：不受随机单目标系统影响，
            // 目标强制为主玩家——洗入副玩家空壳牌库的牌会丢（2026-08-14 用户实测）。
            // 判定优先于 AOE（精英/Boss 战洗入类同样强制主玩家，不双份洗入）。
            if (DualCharacterTargeting.IsShuffleInIntent(entity.Monster))
            {
                var mainCreature = DualCharacterState.MainPlayer?.Creature;
                if (mainCreature != null && !mainCreature.IsDead)
                {
                    __args[0] = new[] { mainCreature };
                }
                return true;
            }

            var target = DualCharacterTargeting.GetOrRollTarget(entity, combatState);
            if (target != null)
            {
                __args[0] = new[] { target };
            }
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 意图目标前缀异常: {e}");
        }
        return true;
    }

    /// <summary>
    /// 攻击意图目标小头像（2026-08-15 用户需求重新实现）：
    /// NCreature.UpdateIntent 完成后，对每个 AttackIntent 对应的 NIntent 子节点，
    /// 在意图图标右侧显示目标角色的小头像；AOE/非攻击意图/无单目标时隐藏。
    ///
    /// ⚠️ 与 2026-08-14 旧实现的两处关键差异（旧版曾造成右下角巨大图片）：
    ///   1. 头像挂到 NIntent 场景内的 %IntentHolder（随意图一起上下浮动/淡入淡出），
    ///      而不是 IntentContainer——不破坏 NCreature.UpdateIntent 按「前 i 个子节点都是
    ///      NIntent」的复用/清理逻辑；
    ///   2. TextureRect 显式 ExpandMode=IgnoreSize + KeepAspectCentered，把选人页大图
    ///      真正缩放为 28×28，旧版未设置 ExpandMode → 按原图尺寸渲染成巨大图片。
    /// </summary>
    private static void NCreatureUpdateIntentScanPostfix(NCreature __instance, object[] __args)
    {
        try
        {
            if (!DualCharacterState.Enabled || __instance == null || __args == null || __args.Length < 1) return;
            if (__args[0] is not IEnumerable<Creature> targets) return;

            var entity = __instance.Entity;
            if (entity == null || entity.Monster == null) return;

            // 单目标已由 Prefix 把 targets 替换为 [目标]；AOE 保持 2 名玩家（不显示头像）。
            var targetList = targets as IReadOnlyList<Creature> ?? targets.ToList();
            var target = targetList.Count == 1 ? targetList[0] : null;

            var intents = entity.Monster.NextMove?.Intents;
            if (intents == null) return;

            var intentContainer = __instance.IntentContainer;
            if (intentContainer == null) return;
            var intentNodes = intentContainer.GetChildren().OfType<NIntent>().ToList();

            for (var i = 0; i < intents.Count && i < intentNodes.Count; i++)
            {
                if (intents[i] is AttackIntent)
                    UpdateIntentAvatar(intentNodes[i], target);
                else
                    UpdateIntentAvatar(intentNodes[i], null);
            }
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 攻击意图头像后缀异常: {e}");
        }
    }

    private const string IntentAvatarNodeName = "ForeveIntentTargetAvatar";
    private static readonly Vector2 IntentAvatarSize = new(28f, 28f);

    /// <summary>更新单个攻击意图节点的目标头像（target=null 时隐藏）。</summary>
    private static void UpdateIntentAvatar(NIntent intentNode, Creature? target)
    {
        if (intentNode == null || !GodotObject.IsInstanceValid(intentNode)) return;

        var holder = intentNode.GetNodeOrNull<Control>("%IntentHolder");
        if (holder == null) return;

        // 头像挂在 holder 下，查找也必须从 holder 下找（避免每次 UpdateIntent 重复创建）。
        var avatar = holder.GetNodeOrNull<TextureRect>(IntentAvatarNodeName);
        var show = target != null && target.IsPlayer;
        if (!show)
        {
            if (avatar != null) avatar.Visible = false;
            return;
        }

        var player = target?.Player;
        var texture = ResolveCharacterAvatarTexture(player?.Character);
        if (texture == null)
        {
            if (avatar != null) avatar.Visible = false;
            return;
        }

        if (avatar == null)
        {
            avatar = new TextureRect
            {
                Name = IntentAvatarNodeName,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = IntentAvatarSize,
                Size = IntentAvatarSize,
                ZIndex = 100,
                ZAsRelative = false,
            };
            holder.AddChild(avatar);
        }
        else
        {
            avatar.Texture = texture;
        }

        avatar.Visible = true;
        // 定位到意图图标右侧、垂直居中（holder 本身会上下浮动，头像作为其子节点自动跟随）。
        // holder.Size 为 0 时（个别意图场景根节点不设尺寸）回退 NIntent.Size。
        var anchorSize = holder.Size;
        if (anchorSize.X < 4f) anchorSize = intentNode.Size;
        avatar.Position = new Vector2(
            anchorSize.X + 4f,
            (anchorSize.Y - IntentAvatarSize.Y) / 2f);
    }

    /// <summary>
    /// 角色小头像纹理：优先 CharacterModel.IconTexture（顶栏同款小头像；
    /// mod 角色通过 CharacterAssetProfile.Ui.IconTexturePath 指向 图像资源\角色头像
    /// 复制进 mod 的 ogier_portrait/rotan_portrait，原版角色为 ui/top_panel 小头像）。
    /// IconTexture 不可用时回退 CharacterSelectIcon（仍会按 28×28 缩放）。
    /// </summary>
    private static Texture2D? ResolveCharacterAvatarTexture(CharacterModel? character)
    {
        if (character == null) return null;

        try
        {
            var texture = character.IconTexture;
            if (texture != null) return texture;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 攻击意图头像: IconTexture 读取异常: {e.Message}");
        }

        try
        {
            var texture = character.CharacterSelectIcon;
            if (texture != null) return texture;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 攻击意图头像: CharacterSelectIcon 读取异常: {e.Message}");
        }

        return null;
    }

    /// <summary>
    /// 双人模式副玩家：CheckIfDrawIsPossibleAndShowThoughtBubbleIfNot 返回 false（不可抽）。
    /// 效果：不显示「没有足够的牌抽取」气泡；Draw 状态机判断不可抽后跳过抽牌循环 →
    /// ShuffleIfNecessary/Shuffle/Add 整条链不执行 → Shuffle NRE 消失。
    /// </summary>
    [HarmonyPriority(Priority.First)]
    private static bool CheckIfDrawIsPossibleAndShowThoughtBubbleIfNotPrefix(Player player, ref bool __result)
    {
        if (!DualCharacterState.Enabled || !DualCharacterState.IsSecondaryPlayer(player)) return true;
        // 2026-08-17：副角色侧卡牌效果结算中（owner 交换或副玩家自有生成卡）的抽牌是卡牌效果，
        // 牌堆重定向已把抽牌/弃牌堆解析到主玩家，按真实牌量判断（否则重逢等卡的抽牌被静默清零）。
        if (DualCharacterCardOwnerPatch.IsSecondaryCardEffectActive) return true;
        __result = false;
        return false;
    }

    /// <summary>
    /// 双人模式副玩家：Draw 壳方法 count 置 0（双保险，独立于 CheckIfDraw 前缀）。
    /// drawsRequested=Ceiling(0)=0 → 抽牌循环 0 次 → ShuffleIfNecessary 不触发。
    /// 2026-08-17：owner 交换窗口内不置 0（卡牌效果抽牌走主玩家牌堆重定向，正常抽）。
    /// 2026-08-23：主玩家死亡但副玩家存活时进入 Draw 死亡重定向 scope（__state=true），
    /// 让 CardPileCmd.Add 的 card.Owner.Creature.IsDead 守卫看到存活副玩家。
    /// </summary>
    private static bool CardPileCmdDrawPrefix(PlayerChoiceContext choiceContext, ref decimal count, Player player, bool fromHandDraw, out bool __state)
    {
        __state = false;
        if (!DualCharacterState.Enabled) return true;

        // 主玩家死亡 + 副玩家存活：Draw 期间把主玩家 creature 重定向到副玩家，保证
        // Draw → CardPileCmd.Add(card, Hand) 不会因 card.Owner.Creature.IsDead 拒绝入牌。
        // ⚠️ 必须在副玩家分支之前建立：卡牌 owner 交换期间（副角色卡牌抽牌）player 参数是
        // 副玩家，但牌堆重定向到主玩家、抽出的牌 owner 仍是主玩家，同样需要本重定向。
        __state = TryEnterDrawDeadMainCreatureScope();

        if (DualCharacterState.IsSecondaryPlayer(player))
        {
            // 2026-08-17：副角色侧卡牌效果结算中（owner 交换或副玩家自有生成卡）的抽牌是卡牌效果，
            // 牌堆重定向已把抽牌/弃牌堆解析到主玩家，按真实牌量判断（否则重逢等卡的抽牌被静默清零）。
            if (!DualCharacterCardOwnerPatch.IsSecondaryCardEffectActive) count = 0m;
        }
        return true;
    }

    /// <summary>Draw 壳方法返回 Task 后：本次 Draw 自己建立的重定向在任务完成（含异常）后解除。</summary>
    private static void CardPileCmdDrawPostfix(Player player, bool __state, ref Task<IEnumerable<CardModel>> __result)
    {
        try
        {
            if (!__state || __result == null) return;
            var main = DualCharacterState.MainPlayer;
            if (main == null)
            {
                ExitDrawDeadMainCreatureScope(null);
                return;
            }
            __result = RestoreDrawDeadMainCreatureScopeAfterAsync(__result, main);
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] Draw 死亡重定向后缀异常: {e.Message}");
        }
    }

    /// <summary>
    /// 进入抽牌死亡重定向 scope：双人模式 && 主玩家 creature 已死 && 副玩家 creature 存活。
    /// 若已有同玩家 scope（嵌套/并发抽牌）则只增加深度；若 Setup/Flush 的临时重定向已生效
    /// （此时 get_Creature 返回副玩家），无需新建 —— Setup/Flush 包装任务会自己恢复。
    /// </summary>
    private static bool TryEnterDrawDeadMainCreatureScope()
    {
        try
        {
            if (!DualCharacterState.Enabled) return false;
            var main = DualCharacterState.MainPlayer;
            var secondary = DualCharacterState.SecondaryPlayer;
            if (main == null || secondary == null) return false;

            if (DrawDeadMainCreatureScopes.TryGetValue(main, out var depth) && depth > 0)
            {
                DrawDeadMainCreatureScopes.AddOrUpdate(main, 1, (_, existing) => existing + 1); // 嵌套抽牌：延长既有 scope
                return true;
            }

            // Setup/Flush 已重定向时 getter 返回副玩家（存活），这里会视为“无需新建”，
            // 该重定向由 Setup/Flush 的任务包装负责恢复，覆盖整个 Draw。
            var mainCreature = main.Creature;
            if (mainCreature == null || !mainCreature.IsDead) return false;

            var secondaryCreature = secondary.Creature;
            if (secondaryCreature == null || secondaryCreature.IsDead) return false;

            DrawDeadMainCreatureScopes.AddOrUpdate(main, 1, (_, existing) => existing + 1);
            return true;
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] TryEnterDrawDeadMainCreatureScope 异常: {e.Message}");
            return false;
        }
    }

    /// <summary>退出抽牌死亡重定向 scope（计数归零时移除）。main 为 null 时清理主玩家条目。</summary>
    private static void ExitDrawDeadMainCreatureScope(Player? main)
    {
        try
        {
            if (main == null) main = DualCharacterState.MainPlayer;
            if (main == null) return;
            var remaining = DrawDeadMainCreatureScopes.AddOrUpdate(
                main, 0, (_, existing) => existing > 0 ? existing - 1 : 0);
            if (remaining <= 0)
            {
                DrawDeadMainCreatureScopes.TryRemove(main, out _);
            }
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] ExitDrawDeadMainCreatureScope 异常: {e.Message}");
        }
    }

    /// <summary>等待原 Draw 任务完成后解除抽牌死亡重定向（异常/取消也恢复）。</summary>
    private static async Task<IEnumerable<CardModel>> RestoreDrawDeadMainCreatureScopeAfterAsync(
        Task<IEnumerable<CardModel>> original, Player main)
    {
        try
        {
            return await original;
        }
        finally
        {
            ExitDrawDeadMainCreatureScope(main);
        }
    }

    // ── 3) 主玩家死亡保护 ──────────────────────────────────────────────────

    private static void InstallDeathProtectionPatches(Harmony harmony)
    {
        var handleDeath = AccessTools.Method(typeof(CombatManager), "HandlePlayerDeath", new[] { typeof(Player) });
        var endTurn = AccessTools.Method(typeof(PlayerCmd), "EndTurn",
            new[] { typeof(Player), typeof(bool), typeof(Func<Task>) });
        harmony.Patch(handleDeath, prefix: new HarmonyMethod(GetMethod(nameof(HandlePlayerDeathPrefix))));
        harmony.Patch(endTurn, prefix: new HarmonyMethod(GetMethod(nameof(PlayerCmdEndTurnPrefix))));

        // 2026-08-16「主角色死亡后进入回合无法抽牌」修复（方法级兜底，不依赖状态机 IL 编号）：
        // SetupPlayerTurn / FlushPlayerHand 两个 async 壳方法执行期间，把死亡主玩家的
        // get_Creature 临时重定向到存活副玩家 creature。原版开头的 IsDead 检查看到“存活”，
        // SetupPlayerTurn 正常重置能量并抽牌，FlushPlayerHand 正常弃牌/清理 ——
        // 后者不修的话，死亡主玩家手牌永远不清空，下一回合因手牌满而抽不了牌。
        var playerCreatureGetter = AccessTools.PropertyGetter(typeof(Player), nameof(Player.Creature));
        harmony.Patch(playerCreatureGetter,
            prefix: new HarmonyMethod(GetMethod(nameof(PlayerCreatureOverridePrefix))));

        // 2026-08-23「主角色死亡后遗物/卡牌 hook 效果不触发」修复：
        // 主玩家死亡时原版 Player.DeactivateHooks 会把 IsActiveForHooks 置 false，
        // CombatState.IterateHookListeners 因此不再遍历主玩家的遗物/药水/手牌卡等模型。
        // 双人模式下副玩家还活着、共享牌库/药水继续可用，主玩家的模型 hooks 必须保持激活。
        var deactivateHooks = AccessTools.Method(typeof(Player), nameof(Player.DeactivateHooks));
        harmony.Patch(deactivateHooks, prefix: new HarmonyMethod(GetMethod(nameof(DeactivateHooksPrefix))));

        // 2026-08-24「战斗结束死亡角色自动复活」修复：
        // CombatManager.EndCombatInternal 会遍历 Players 调 ReviveBeforeCombatEnd（原版多人规则：
        // 战斗结束给死亡玩家回 1 血，保证其遗物能收到 AfterCombatEnd）。双人模式预期死亡保持到
        // 篝火复活/下一层，因此跳过该方法。
        var reviveBeforeCombatEnd = AccessTools.Method(typeof(Player), nameof(Player.ReviveBeforeCombatEnd));
        harmony.Patch(reviveBeforeCombatEnd, prefix: new HarmonyMethod(GetMethod(nameof(ReviveBeforeCombatEndPrefix))));

        var setupShell = AccessTools.Method(typeof(CombatManager), "SetupPlayerTurn",
            new[] { typeof(Player), typeof(HookPlayerChoiceContext) });
        harmony.Patch(setupShell,
            prefix: new HarmonyMethod(GetMethod(nameof(SetupPlayerTurnOverridePrefix))),
            postfix: new HarmonyMethod(GetMethod(nameof(SetupPlayerTurnOverridePostfix))));

        var flushShell = AccessTools.Method(typeof(CombatManager), "FlushPlayerHand",
            new[] { typeof(Player), typeof(HookPlayerChoiceContext) });
        harmony.Patch(flushShell,
            prefix: new HarmonyMethod(GetMethod(nameof(FlushPlayerHandOverridePrefix))),
            postfix: new HarmonyMethod(GetMethod(nameof(FlushPlayerHandOverridePostfix))));

        // 主玩家死亡后仍要进入回合：SetupPlayerTurn d__104 开头有
        // player.Creature.IsDead → 直接 return 的跳过。双人模式下主玩家死亡也不能跳过
        // （共享手牌/能量都在主玩家 PCS 里，跳过 = 无回合可打）。
        var setupD104 = typeof(CombatManager).GetNestedType("<SetupPlayerTurn>d__104", BindingFlags.NonPublic);
        var setupMoveNext = setupD104?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
        if (setupMoveNext != null)
        {
            harmony.Patch(setupMoveNext, transpiler: new HarmonyMethod(GetMethod(nameof(SetupPlayerTurnDeathCheckTranspiler))));
        }

        // StartTurn d__102 结尾还有一次死亡检查：dead player 跳过 RunAutoPrePlayPhase
        // （PlayerCombatState.Phase 停在 Start，不进入 Play）。双人模式主玩家死亡时也不能跳过。
        var startD102 = typeof(CombatManager).GetNestedType("<StartTurn>d__102", BindingFlags.NonPublic);
        var startMoveNext = startD102?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
        if (startMoveNext != null)
        {
            harmony.Patch(startMoveNext, transpiler: new HarmonyMethod(GetMethod(nameof(StartTurnAutoPrePlayDeathCheckTranspiler))));
        }

        // 结束回合按钮：PlayerCanTakeAction 要求本地玩家 Creature.IsAlive。
        // 主玩家死亡、副玩家存活时视为可行动（存活角色替死亡角色打牌/结束回合）。
        var playerCanTakeAction = AccessTools.Method(typeof(NEndTurnButton), "PlayerCanTakeAction",
            new[] { typeof(Player) });
        if (playerCanTakeAction != null)
        {
            harmony.Patch(playerCanTakeAction,
                prefix: new HarmonyMethod(GetMethod(nameof(PlayerCanTakeActionPrefix))));
        }

        // 2026-08-23「主角色死亡后不能使用药水」修复：
        // NPotionPopup._Ready / RefreshButtons 都会因 Potion.Owner.Creature.IsDead 禁用按钮。
        // 药水已合并到主玩家，副玩家存活时应对主玩家临时重定向 creature 再走原版按钮逻辑。
        InstallPotionPopupDeathPatches(harmony);

        _logger?.Info($"[Foreve][Dual] 死亡保护 patch 已装 (HandlePlayerDeath={handleDeath != null}, PlayerCmd.EndTurn={endTurn != null}, " +
                      $"Player.get_Creature重定向={playerCreatureGetter != null}, DeactivateHooks保持={deactivateHooks != null}, 战斗结束不复活={reviveBeforeCombatEnd != null}, SetupPlayerTurn壳={setupShell != null}, FlushPlayerHand壳={flushShell != null}, " +
                      $"SetupPlayerTurn.d104={setupMoveNext != null}, StartTurn.d102={startMoveNext != null}, " +
                      $"NEndTurnButton.PlayerCanTakeAction={playerCanTakeAction != null}, 药水弹窗死亡重定向={_potionPopupPatchesInstalled})");
    }

    /// <summary>双人模式：跳过 HandlePlayerDeath —— 不清空共享手牌/能量/星（副玩家活着还能打）。</summary>
    private static bool HandlePlayerDeathPrefix(Player player)
    {
        if (DualCharacterState.Enabled) return false;
        return true;
    }

    /// <summary>
    /// 双人模式主玩家死亡且副玩家存活：不关闭主玩家的模型 hooks。
    /// 原版 DeactivateHooks 会把 IsActiveForHooks=false，CombatState.IterateHookListeners 随之
    /// 跳过主玩家的遗物/药水/手牌等模型 → 遗物抽牌等 hook 效果不再触发。主玩家只是“操作者”死亡，
    /// 共享牌库/遗物/药水由存活副角色继续使用，因此 hooks 必须保持激活。
    /// </summary>
    private static bool DeactivateHooksPrefix(Player __instance)
    {
        try
        {
            if (!DualCharacterState.Enabled || __instance == null) return true;
            if (!DualCharacterState.IsMainPlayer(__instance)) return true;
            if (DualCharacterState.SecondaryPlayer?.Creature is { IsDead: false }) return false;
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] DeactivateHooks 前缀异常: {e.Message}");
        }
        return true;
    }

    /// <summary>
    /// 战斗开始时恢复主玩家 hooks（死亡保持 + 下一场战斗续战场景）：
    /// 上一场战斗死亡的主玩家 IsActiveForHooks=false；副玩家存活时主玩家共享牌库/遗物仍要工作。
    /// </summary>
    private static void ActivateMainHooksIfSecondaryAlive()
    {
        try
        {
            if (!DualCharacterState.Enabled) return;
            var main = DualCharacterState.MainPlayer;
            var secondary = DualCharacterState.SecondaryPlayer;
            if (main?.Creature is { IsDead: true } && secondary?.Creature is { IsDead: false })
            {
                main.ActivateHooks();
            }
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] 战斗开始恢复主玩家 hooks 异常: {e.Message}");
        }
    }

    /// <summary>
    /// 双人模式：跳过原版「战斗结束前把死亡玩家奶到 1 血」的多人规则。
    /// 预期：死亡角色在本场战斗结束后保持死亡，直到篝火复活或下一层等 mod 复活点。
    /// </summary>
    private static bool ReviveBeforeCombatEndPrefix(Player __instance, ref Task __result)
    {
        if (DualCharacterState.Enabled)
        {
            __result = Task.CompletedTask; // Prefix 跳过 async 方法时必须给回一个已完成 Task
            return false;
        }
        return true;
    }

    // ── 3.05) 药水弹窗死亡重定向（主玩家死亡后药水仍可用） ─────────────────────

    /// <summary>
    /// NPotionPopup._Ready 与 RefreshButtons 在双人模式主玩家死亡时会因
    /// Potion.Owner.Creature.IsDead 禁用使用/丢弃按钮。这里对两个方法做「同步临时重定向」：
    /// 执行期间把主玩家 get_Creature 指向存活副玩家，原版的 Usage / CombatOnly / 行动禁用 /
    /// 卡牌选择界面等判断全部照常运行，方法返回后立即恢复。
    /// </summary>
    private static void InstallPotionPopupDeathPatches(Harmony harmony)
    {
        try
        {
            var ready = AccessTools.Method(typeof(NPotionPopup), "_Ready");
            var refresh = AccessTools.Method(typeof(NPotionPopup), "RefreshButtons");
            var popupCount = 0;

            if (ready != null)
            {
                harmony.Patch(ready,
                    prefix: new HarmonyMethod(GetMethod(nameof(NPotionPopupReadyPrefix))),
                    postfix: new HarmonyMethod(GetMethod(nameof(NPotionPopupReadyPostfix))));
                popupCount++;
            }
            if (refresh != null)
            {
                harmony.Patch(refresh,
                    prefix: new HarmonyMethod(GetMethod(nameof(NPotionPopupRefreshButtonsPrefix))),
                    postfix: new HarmonyMethod(GetMethod(nameof(NPotionPopupRefreshButtonsPostfix))));
                popupCount++;
            }

            // 按钮可用只是第一步；Self 药水/使用 VFX 的 Owner.Creature 也要落到存活副角色。
            var onUse = AccessTools.Method(typeof(PotionModel), "OnUseWrapper",
                new[] { typeof(PlayerChoiceContext), typeof(Creature) });
            if (onUse != null)
            {
                harmony.Patch(onUse,
                    prefix: new HarmonyMethod(GetMethod(nameof(PotionOnUseWrapperPrefix))),
                    postfix: new HarmonyMethod(GetMethod(nameof(PotionOnUseWrapperPostfix))));
            }

            // 抽牌药水（共享牌堆）在双人模式下不应弹玩家目标选择：
            // CanThrowAtAlly=false → NPotionHolder.UsePotion 走无目标分支（按钮文案也变回 drink）；
            // EnqueueManualUse 再把 null 目标补成主玩家，保证 OnUse 的 target.Player 指向共享牌堆。
            var canThrowAtAlly = AccessTools.Method(typeof(PotionModel), nameof(PotionModel.CanThrowAtAlly));
            if (canThrowAtAlly != null)
            {
                harmony.Patch(canThrowAtAlly, prefix: new HarmonyMethod(GetMethod(nameof(PotionCanThrowAtAllyPrefix))));
            }
            var enqueueManualUse = AccessTools.Method(typeof(PotionModel), nameof(PotionModel.EnqueueManualUse),
                new[] { typeof(Creature) });
            if (enqueueManualUse != null)
            {
                harmony.Patch(enqueueManualUse, prefix: new HarmonyMethod(GetMethod(nameof(PotionEnqueueManualUsePrefix))));
            }

            _potionPopupPatchesInstalled = popupCount == 2 && onUse != null && canThrowAtAlly != null && enqueueManualUse != null;
            if (!_potionPopupPatchesInstalled)
            {
                _logger?.Warn($"[Foreve][Dual] 药水死亡可用性/抽牌药水无目标 patch 部分缺失 (Ready={ready != null}, RefreshButtons={refresh != null}, OnUseWrapper={onUse != null}, CanThrowAtAlly={canThrowAtAlly != null}, EnqueueManualUse={enqueueManualUse != null})");
            }
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] 药水弹窗死亡重定向安装异常: {e}");
        }
    }

    /// <summary>读取药水弹窗当前绑定的药水 Owner（私有 Potion 属性，反射读取）。</summary>
    private static Player? GetPotionPopupOwner(NPotionPopup popup)
    {
        try
        {
            if (popup == null) return null;
            var potion = Traverse.Create(popup).Property<PotionModel>("Potion").Value;
            return potion?.Owner;
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] 药水弹窗 Owner 读取异常: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 共享牌堆/能量类的抽牌药水：双人模式下所有牌都在主玩家牌堆，目标选择没有意义。
    /// 覆盖原版 SwiftPotion/BottledPotential/Clarity/CureAll/SneckoOil 与 mod 无尽战意。
    /// </summary>
    private static bool IsSharedDrawPotion(PotionModel potion)
    {
        return potion is SwiftPotion
            or BottledPotential
            or Clarity
            or CureAll
            or SneckoOil
            or RotanInexhaustibleFervor;
    }

    /// <summary>抽牌药水在双人模式下返回 CanThrowAtAlly=false：不进入玩家目标选择，按钮显示为 drink。</summary>
    private static bool PotionCanThrowAtAllyPrefix(PotionModel __instance, ref bool __result)
    {
        try
        {
            if (DualCharacterState.Enabled && __instance != null && IsSharedDrawPotion(__instance))
            {
                __result = false;
                return false;
            }
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] PotionCanThrowAtAlly 前缀异常: {e.Message}");
        }
        return true;
    }

    /// <summary>
    /// CanThrowAtAlly=false 后，NPotionHolder.UsePotion 会以 null 目标入队；
    /// 这里把抽牌药水的 null 目标补成主玩家（共享牌堆 owner）。主玩家死亡时也强制指向主玩家：
    /// 抽牌必须走主玩家牌堆，OnUse 的 target.Player 不能是副玩家。
    /// </summary>
    private static void PotionEnqueueManualUsePrefix(PotionModel __instance, ref Creature target)
    {
        try
        {
            if (!DualCharacterState.Enabled || __instance == null || !IsSharedDrawPotion(__instance) || target != null) return;
            var mainCreature = DualCharacterState.MainPlayer?.Creature;
            if (mainCreature != null) target = mainCreature;
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] PotionEnqueueManualUse 前缀异常: {e.Message}");
        }
    }

    /// <summary>
    /// 尝试为药水弹窗逻辑建立主玩家 creature 重定向。已存在其他重定向（Setup/Flush/Draw）时
    /// 不重复添加（__state=false），由既有 scope 负责恢复；本方法只负责自己添加的条目。
    /// </summary>
    private static bool TryBeginPotionPopupCreatureRedirect(NPotionPopup popup)
    {
        try
        {
            if (!DualCharacterState.Enabled) return false;
            var owner = GetPotionPopupOwner(popup);
            if (owner == null || !DualCharacterState.IsMainPlayer(owner)) return false;
            if (DeadMainCreatureOverrides.ContainsKey(owner)) return false; // 已有其他 scope，别抢恢复权

            if (owner.Creature is not { IsDead: true }) return false;
            var secondary = DualCharacterState.SecondaryPlayer?.Creature;
            if (secondary == null || secondary.IsDead) return false;

            return DeadMainCreatureOverrides.TryAdd(owner, secondary);
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] 药水弹窗 creature 重定向建立异常: {e.Message}");
            return false;
        }
    }

    private static void EndPotionPopupCreatureRedirect()
    {
        try
        {
            var main = DualCharacterState.MainPlayer;
            if (main != null) DeadMainCreatureOverrides.TryRemove(main, out _);
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] 药水弹窗 creature 重定向恢复异常: {e.Message}");
        }
    }

    private static bool NPotionPopupReadyPrefix(NPotionPopup __instance, out bool __state)
    {
        __state = TryBeginPotionPopupCreatureRedirect(__instance);
        return true;
    }

    private static void NPotionPopupReadyPostfix(bool __state)
    {
        if (__state) EndPotionPopupCreatureRedirect();
    }

    private static bool NPotionPopupRefreshButtonsPrefix(NPotionPopup __instance, out bool __state)
    {
        __state = TryBeginPotionPopupCreatureRedirect(__instance);
        return true;
    }

    private static void NPotionPopupRefreshButtonsPostfix(bool __state)
    {
        if (__state) EndPotionPopupCreatureRedirect();
    }

    /// <summary>
    /// 药水实际使用（OnUseWrapper）也做同款重定向：主玩家死亡后，药水仍归主玩家（药水已合并），
    /// 但使用者的 creature/VFX 源、以及 Self 药水的目标都应落到存活副角色，避免“按钮能点、
    /// 效果打到尸体上”。
    /// </summary>
    private static bool PotionOnUseWrapperPrefix(PotionModel __instance, ref Creature target, out bool __state)
    {
        __state = false;
        try
        {
            if (!DualCharacterState.Enabled || __instance == null) return true;
            var owner = __instance.Owner;
            if (owner == null || !DualCharacterState.IsMainPlayer(owner)) return true;

            var mainCreature = owner.Creature;
            if (mainCreature == null || !mainCreature.IsDead) return true;
            var secondary = DualCharacterState.SecondaryPlayer?.Creature;
            if (secondary == null || secondary.IsDead) return true;

            // Self 药水在 UsePotion 阶段已把目标固定为死亡主角色，这里改指存活副角色。
            // 抽牌类药水即使主角色死亡也必须保留主玩家 target（共享牌堆），否则会抽副玩家空牌堆。
            if (target != null && ReferenceEquals(target, mainCreature)
                && __instance.TargetType == MegaCrit.Sts2.Core.Entities.Cards.TargetType.Self)
            {
                target = secondary;
            }

            __state = TryOverrideDeadMainCreature(owner, "PotionOnUseWrapper");
            return true;
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] PotionOnUseWrapper 前缀异常: {e.Message}");
            return true;
        }
    }

    /// <summary>OnUseWrapper 返回 Task 后：等待药水结算完成后恢复 creature 重定向。</summary>
    private static void PotionOnUseWrapperPostfix(PotionModel __instance, bool __state, ref Task __result)
    {
        try
        {
            if (!__state || __result == null) return;
            var main = __instance?.Owner ?? DualCharacterState.MainPlayer;
            if (main == null) return;
            __result = RestoreCreatureOverrideAfterAsync(__result, main);
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] PotionOnUseWrapper 后缀异常: {e.Message}");
        }
    }

    // ── 3.1) 死亡主玩家 creature 临时重定向（Setup/Flush/药水结算走原版全流程） ─────────

    /// <summary>
    /// Player.get_Creature 重定向：SetupPlayerTurn / FlushPlayerHand / 药水弹窗与 OnUseWrapper
    /// 执行期间表里才会有条目，其余时间零变化。Prefix 里绝不回读 player.Creature（避免 Harmony
    /// getter 重入）。
    /// </summary>
    [HarmonyPriority(Priority.First)]
    private static bool PlayerCreatureOverridePrefix(Player __instance, ref Creature __result)
    {
        if (__instance == null) return true;
        // 1) 死亡主玩家保护（SetupPlayerTurn/FlushPlayerHand/药水弹窗/药水结算表内条目）
        if (DeadMainCreatureOverrides.Count > 0
            && DeadMainCreatureOverrides.TryGetValue(__instance, out var aliveCreature))
        {
            __result = aliveCreature;
            return false;
        }
        // 1b) 抽牌效果执行期间（2026-08-23）：主玩家 creature 临时重定向到存活副玩家，
        //     让 CardPileCmd.Add 的 card.Owner.Creature.IsDead 守卫放行主牌堆里的牌。
        if (DrawDeadMainCreatureScopes.TryGetValue(__instance, out var drawDepth) && drawDepth > 0)
        {
            var drawAliveCreature = DualCharacterState.SecondaryPlayer?.Creature;
            if (drawAliveCreature != null && !drawAliveCreature.IsDead)
            {
                __result = drawAliveCreature;
                return false;
            }
        }
        // 2) 遗物装备作用域（2026-08-18 遗物系统重做）：角色向遗物钩子/拾起效果执行期间，
        //    主玩家 get_Creature 重定向到该遗物的装备角色 —— 原版遗物读 Owner.Creature 即落在装备者身上。
        //    与 1) 条件互斥（1) 只在死亡保护窗口存在）。
        if (Foreve.Scripts.DualCharacter.DualCharacterRelicEquip.TryGetScopedCreatureRedirect(__instance, out var equippedCreature))
        {
            __result = equippedCreature;
            return false;
        }
        return true;
    }

    /// <summary>SetupPlayerTurn 壳方法进入时：死亡主玩家 + 存活副玩家 → 建立 creature 重定向。</summary>
    private static bool SetupPlayerTurnOverridePrefix(Player player)
    {
        TryOverrideDeadMainCreature(player, "SetupPlayerTurn");
        return true;
    }

    /// <summary>SetupPlayerTurn 壳方法返回 Task 后：包装为“原任务完成后恢复 creature”的 Task。</summary>
    private static void SetupPlayerTurnOverridePostfix(Player player, ref Task __result)
    {
        if (player == null || __result == null || !DeadMainCreatureOverrides.ContainsKey(player)) return;
        __result = RestoreCreatureOverrideAfterAsync(__result, player);
    }

    /// <summary>FlushPlayerHand 壳方法进入时：同上（保证死亡主玩家手牌正常弃牌，下回合才有空间抽牌）。</summary>
    private static bool FlushPlayerHandOverridePrefix(Player player)
    {
        TryOverrideDeadMainCreature(player, "FlushPlayerHand");
        return true;
    }

    /// <summary>FlushPlayerHand 壳方法返回 Task 后：包装恢复。</summary>
    private static void FlushPlayerHandOverridePostfix(Player player, ref Task __result)
    {
        if (player == null || __result == null || !DeadMainCreatureOverrides.ContainsKey(player)) return;
        __result = RestoreCreatureOverrideAfterAsync(__result, player);
    }

    /// <summary>
    /// 建立重定向：双人模式 && 主玩家 creature 已死 && 副玩家 creature 存活。
    /// 表里已有同玩家条目时 TryAdd 失败（返回 false），沿用已有重定向，避免覆盖。
    /// </summary>
    private static bool TryOverrideDeadMainCreature(Player player, string caller)
    {
        try
        {
            if (!DualCharacterState.Enabled || player == null) return false;
            if (!DualCharacterState.IsMainPlayer(player)) return false;

            var mainCreature = player.Creature;
            if (mainCreature == null || !mainCreature.IsDead) return false;

            var secondaryCreature = DualCharacterState.SecondaryPlayer?.Creature;
            if (secondaryCreature == null || secondaryCreature.IsDead) return false;

            if (!DeadMainCreatureOverrides.TryAdd(player, secondaryCreature)) return false;
            GD.Print($"[Foreve][Dual][DeathRedirect] {caller}: 主玩家 creature 已死 → 临时重定向到存活副玩家 (netId={player.NetId})");
            return true;
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] TryOverrideDeadMainCreature({caller}) 异常: {e.Message}");
            return false;
        }
    }

    /// <summary>等待原始 Setup/Flush 任务完成后移除重定向（异常/取消也恢复）。</summary>
    private static async Task RestoreCreatureOverrideAfterAsync(Task original, Player player)
    {
        try
        {
            await original;
        }
        finally
        {
            if (DeadMainCreatureOverrides.TryRemove(player, out _))
            {
                GD.Print($"[Foreve][Dual][DeathRedirect] 已恢复主玩家 creature (netId={player.NetId})");
            }
        }
    }

    /// <summary>
    /// 双人模式 && 玩家已死 && (false, null) 形态（= CreatureCmd.Kill d__14 的死亡强制结束回合）
    /// → 跳过，死亡不强制结束共享回合。结束回合按钮（true, null）/虚空形态（false, null，活人打出）
    /// 不受影响。
    /// </summary>
    private static bool PlayerCmdEndTurnPrefix(Player player, bool canBackOut, Func<Task> actionDuringEnemyTurn)
    {
        if (DualCharacterState.Enabled && player != null && player.Creature != null
            && player.Creature.IsDead && !canBackOut && actionDuringEnemyTurn == null)
            return false;
        return true;
    }

    /// <summary>
    /// SetupPlayerTurn d__104 开头死亡跳过判定替换：
    /// 原 IL：ldfld player → callvirt Player.get_Creature → callvirt Creature.get_IsDead → brfalse
    /// 替换为：ldfld player → call ShouldSkipDeadPlayerTurnSetup(player) → brfalse。
    /// 双人模式主玩家即使死亡也继续 SetupPlayerTurn（共享牌库/能量在主玩家）；
    /// 单玩家局与副玩家行为不变。
    /// </summary>
    private static IEnumerable<CodeInstruction> SetupPlayerTurnDeathCheckTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var helper = AccessTools.Method(typeof(DualCharacterCombatPatches), nameof(ShouldSkipDeadPlayerTurnSetup),
            new[] { typeof(Player) });
        var getCreature = AccessTools.PropertyGetter(typeof(Player), nameof(Player.Creature));
        var isDead = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.IsDead));
        if (helper == null || getCreature == null || isDead == null) return list;

        var patched = false;
        for (var i = 0; i + 1 < list.Count; i++)
        {
            var ci = list[i];
            if (ci.opcode != OpCodes.Callvirt || ci.operand is not MethodInfo getter || getter != getCreature)
                continue;
            if (list[i + 1].opcode != OpCodes.Callvirt || list[i + 1].operand is not MethodInfo deadGetter
                || deadGetter != isDead)
                continue;

            var replacement = new CodeInstruction(OpCodes.Call, helper);
            replacement.labels.AddRange(ci.labels);
            replacement.labels.AddRange(list[i + 1].labels);
            list[i] = replacement;
            list.RemoveAt(i + 1);
            patched = true;
            break;
        }

        if (patched) _logger?.Info("[Foreve][Dual] SetupPlayerTurn 死亡跳过已改造：主玩家死亡仍可进入回合");
        else _logger?.Warn("[Foreve][Dual] SetupPlayerTurn 死亡跳过未找到 - 游戏结构变更?");
        return list;
    }

    /// <summary>SetupPlayerTurn 是否因玩家死亡跳过：双人模式主玩家死亡 → 不跳过（继续共享回合）。</summary>
    private static bool ShouldSkipDeadPlayerTurnSetup(Player player)
    {
        try
        {
            if (player?.Creature == null) return true;
            if (!player.Creature.IsDead) return false;
            if (DualCharacterState.Enabled && DualCharacterState.IsMainPlayer(player)) return false;
            return true;
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] ShouldSkipDeadPlayerTurnSetup 异常: {e.Message}");
            return player?.Creature?.IsDead == true;
        }
    }

    /// <summary>
    /// StartTurn d__102 的 RunAutoPrePlayPhase 前置死亡判定替换：
    /// 原 IL：ldloc player → callvirt Player.get_Creature → callvirt Creature.get_IsDead → brtrue 跳过
    /// 替换为：ldloc player → call ShouldRunAutoPrePlayForPlayer(player) → brtrue。
    /// 双人模式主玩家死亡仍执行 RunAutoPrePlayPhase → PCS.Phase 进入 Play，
    /// 否则卡牌/药水/结束回合等操作保持 Start 阶段不可用。
    /// </summary>
    private static IEnumerable<CodeInstruction> StartTurnAutoPrePlayDeathCheckTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var helper = AccessTools.Method(typeof(DualCharacterCombatPatches), nameof(ShouldRunAutoPrePlayForPlayer),
            new[] { typeof(Player) });
        var getCreature = AccessTools.PropertyGetter(typeof(Player), nameof(Player.Creature));
        var isDead = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.IsDead));
        var runAutoPrePlay = AccessTools.Method(typeof(CombatManager), "RunAutoPrePlayPhase",
            new[] { typeof(HookPlayerChoiceContext), typeof(Task), typeof(Player) });
        if (helper == null || getCreature == null || isDead == null || runAutoPrePlay == null) return list;

        var patched = false;
        for (var i = 0; i + 1 < list.Count; i++)
        {
            var ci = list[i];
            if (ci.opcode != OpCodes.Callvirt || ci.operand is not MethodInfo getter || getter != getCreature)
                continue;
            if (list[i + 1].opcode != OpCodes.Callvirt || list[i + 1].operand is not MethodInfo deadGetter
                || deadGetter != isDead)
                continue;

            // StartTurn 里有三处 get_Creature+get_IsDead 连续对：自动 ready、日志插值、
            // RunAutoPrePlayPhase。只替换后面紧跟 RunAutoPrePlayPhase 调用的那一处。
            var isAutoPrePlayCheck = false;
            var scanEnd = Math.Min(list.Count, i + 12);
            for (var j = i + 2; j < scanEnd; j++)
            {
                if (list[j].opcode == OpCodes.Call && list[j].operand is MethodInfo called
                    && called == runAutoPrePlay)
                {
                    isAutoPrePlayCheck = true;
                    break;
                }
            }
            if (!isAutoPrePlayCheck) continue;

            var replacement = new CodeInstruction(OpCodes.Call, helper);
            replacement.labels.AddRange(ci.labels);
            replacement.labels.AddRange(list[i + 1].labels);
            list[i] = replacement;
            list.RemoveAt(i + 1);
            patched = true;
            break;
        }

        if (patched) _logger?.Info("[Foreve][Dual] StartTurn 死亡跳过已改造：主玩家死亡仍进入 AutoPrePlay/Play 阶段");
        else _logger?.Warn("[Foreve][Dual] StartTurn RunAutoPrePlayPhase 死亡跳过未找到 - 游戏结构变更?");
        return list;
    }

    /// <summary>RunAutoPrePlayPhase 是否执行：双人模式主玩家死亡 → 仍执行（存活角色共用主玩家 PCS）。</summary>
    private static bool ShouldRunAutoPrePlayForPlayer(Player player)
    {
        try
        {
            if (player?.Creature == null) return false;
            if (!player.Creature.IsDead) return true;
            if (DualCharacterState.Enabled && DualCharacterState.IsMainPlayer(player)) return true;
            return false;
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] ShouldRunAutoPrePlayForPlayer 异常: {e.Message}");
            return player?.Creature?.IsDead == false;
        }
    }

    /// <summary>
    /// 结束回合按钮可用性：双人模式主玩家死亡但副玩家存活时，视为主玩家可行动
    /// （由存活角色使用共享手牌/药水并结束回合）。
    /// </summary>
    private static bool PlayerCanTakeActionPrefix(Player player, ref bool __result)
    {
        try
        {
            if (!DualCharacterState.Enabled || player == null) return true;
            if (player.Creature?.IsAlive == true) return true;
            if (!DualCharacterState.IsMainPlayer(player)) return true;

            var secondary = DualCharacterState.SecondaryPlayer;
            if (secondary?.Creature?.IsAlive == true)
            {
                __result = true;
                return false;
            }
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] PlayerCanTakeActionPrefix 异常: {e.Message}");
        }
        return true;
    }

    // ── 4) 敌人随机单目标 + 意图 UI 显示目标角色 + Kill 探针/防御 ─────────────

    private static void InstallTargetingPatches(Harmony harmony)
    {
        var updateIntent = AccessTools.Method(typeof(NCreature), "UpdateIntent", new[] { typeof(IEnumerable<Creature>) });
        harmony.Patch(updateIntent, prefix: new HarmonyMethod(GetMethod(nameof(NCreatureUpdateIntentPrefix))));

        // 动作执行单目标化：MoveState.PerformMove 壳方法 Prefix（方法级，版本漂移安全）。
        // async 壳方法参数替换有效：状态机在 builder.Start 前 stfld 保存改后的 targets。
        var performMove = AccessTools.Method(typeof(MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState), "PerformMove",
            new[] { typeof(IEnumerable<Creature>) });
        harmony.Patch(performMove, prefix: new HarmonyMethod(GetMethod(nameof(MoveStatePerformMovePrefix))));

        // Kill NRE 防御 + 卡死兜底：KillWithoutCheckingWinCondition 壳方法改为
        // InstallScanPatches 里的 GetMethods 扫描式挂载（实测修复轮 7）——轮 6 的精确签名
        // AccessTools.Method 挂载日志全 True 但运行时零输出（版本漂移），Task 吞异常兜底
        // 从未生效 → 死亡 NRE 中断敌人回合管道 → 卡死。扫描式重挂确保兜底真正执行。
        // （optimizer：Kill 状态机 MoveNext Finalizer 兜底已移除——async 状态机内部
        //   try/catch 会把异常吞进 Task，Finalizer 实测永不触发，纯探针无功能。）

        _logger?.Info($"[Foreve][Dual] 敌人目标 patch 已装 (NCreature.UpdateIntent={updateIntent != null}, MoveState.PerformMove={performMove != null}, Kill壳扫描见 InstallScanPatches)");
    }

    /// <summary>
    /// 意图显示时（RefreshIntents → UpdateIntent(Players.Select(Creature))，IL 966325）：
    /// 双人模式为每只怪物掷加权随机单目标并写缓存，targets 替换为 [目标] →
    /// NIntent 数值标签按单目标显示；动作执行时（MoveState.PerformMove 前缀）现掷。
    /// 精英/Boss debuff AOE 招式（IsDebuffAoeMove）→ GetOrRollTarget 返回 null，
    /// targets 保留全量玩家列表（两名全中，意图不标单目标名）。
    /// </summary>
    private static bool NCreatureUpdateIntentPrefix(NCreature __instance, ref IEnumerable<Creature> targets)
    {
        try
        {
            if (!DualCharacterState.Enabled) return true;
            var entity = __instance?.Entity;
            if (entity == null || entity.Monster == null) return true; // 玩家 creature 无意图
            if (targets == null) return true;

            var list = targets as IReadOnlyList<Creature> ?? targets.ToList();
            if (list.Count != 2 || list[0] == null || list[1] == null
                || !list[0].IsPlayer || !list[1].IsPlayer) return true; // 非「全量玩家」不处理

            var combatState = entity.CombatState;
            if (combatState == null || !DualCharacterState.IsDualMode(combatState)) return true;

            var target = DualCharacterTargeting.GetOrRollTarget(entity, combatState);
            if (target != null) targets = new[] { target };
            return true;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] NCreature.UpdateIntent 前缀异常: {e}");
            return true;
        }
    }

    // 意图目标姓名 Label 已按用户要求删除；目标角色改用攻击意图右侧的 28×28 小头像显示，
    // 实现见 NCreatureUpdateIntentScanPostfix / UpdateIntentAvatar（2026-08-15 重新实现）。

    /// <summary>
    /// 动作执行单目标化（2026-08-14 改造，替代失效的 &lt;PerformMove&gt;d__103 transpiler）：
    /// MoveState.PerformMove(IEnumerable&lt;Creature&gt; targets) 壳方法 Prefix，ref targets 单目标化。
    /// - 双人模式判定：从 targets 的 creature 拿 combatState（MoveState 无怪物引用，IL 实证）；
    ///   targets 空/无玩家/非双人 → 放行（零变化）。
    /// - AOE 判定（精英/Boss debuff 招式，两名全中）：moveState.get_Intents() + 房间类型，
    ///   与意图显示共用 DualCharacterTargeting.IsDebuffAoeMove（同一核心 → 意图显示==实际命中）。
    /// - 否则单目标：DualCharacterTargeting.RollTargetForCombat 现掷（与意图显示同 RNG 不同时机），
    ///   返回 null 时放行原 targets。
    /// </summary>
    [HarmonyPriority(Priority.First)]
    private static bool MoveStatePerformMovePrefix(MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState __instance, ref IEnumerable<Creature> targets)
    {
        try
        {
            if (!DualCharacterState.Enabled || targets == null) return true;

            // 物化一次（避免单次遍历枚举器被多次消费）；从 targets 取 combatState
            var list = targets as IReadOnlyList<Creature> ?? targets.ToList();
            ICombatState? combatState = null;
            var hasPlayer = false;
            foreach (var c in list)
            {
                if (c == null) continue;
                if (c.IsPlayer) hasPlayer = true;
                if (combatState == null && c.CombatState != null) combatState = c.CombatState;
            }
            if (combatState == null || !hasPlayer) return true; // 非玩家目标招式（召唤/队友buff等）不干预
            if (!DualCharacterState.IsDualMode(combatState)) return true;

            // 洗入牌库类意图（往手牌/卡组/弃牌堆加牌，轮 8）：目标强制主玩家，
            // 与意图显示侧（NCreatureUpdateIntentScanPrefix）同判定 → 意图显示==实际命中。
            // 判定优先于 AOE（精英/Boss 战洗入类同样强制主玩家，不双份洗入）。
            var intents = __instance?.Intents;
            if (intents != null && DualCharacterTargeting.IsShuffleInIntent(intents))
            {
                var mainCreature = DualCharacterState.MainPlayer?.Creature;
                if (mainCreature != null && !mainCreature.IsDead)
                {
                    targets = new[] { mainCreature };
                }
                return true;
            }

            // 精英/Boss 战 debuff 招式 AOE：两名全中（不改 targets）；意图显示同判定
            if (intents != null && DualCharacterTargeting.IsDebuffAoeMove(intents, combatState)) return true;

            // 普通招式：随机单目标
            var target = DualCharacterTargeting.RollTargetForCombat(combatState);
            if (target != null) targets = new[] { target };
            return true;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] MoveState.PerformMove 前缀异常: {e}");
            return true;
        }
    }

    /// <summary>
    /// KillWithoutCheckingWinCondition 壳方法 Prefix：null 防御（creature == null → return false
    /// 跳过 Kill，防止 NRE 中断死亡流程 → 敌人回合管道中断 → 战斗卡死）。
    /// （optimizer：原无条件 [KillProbe] 打印已移除。）
    /// </summary>
    private static bool KillWithoutCheckingWinConditionPrefix(Creature creature, bool force, int recursion)
    {
        if (creature == null) return false; // null 防御：跳过 Kill
        return true;
    }

    /// <summary>
    /// Kill 卡死兜底（2026-08-14）：双人模式下把 Kill 返回的 Task 换成 OnlyOnFaulted 观察包装
    /// （吞掉异常只打日志，不向 await 链传播）。实测 Kill NRE 会使 Damage → 怪物招式 →
    /// TakeTurn → ExecuteEnemyTurn → StartTurn 整条敌人回合管道中断，战斗停在敌人侧=卡死；
    /// 本兜底保证即使死亡流程还有未知 NRE，敌人回合也能走完（死亡状态不完整但回合不卡）。
    /// Kill NRE 根因修复后正常路径不受影响。
    /// </summary>
    private static void KillWithoutCheckingWinConditionPostfix(ref Task __result)
    {
        try
        {
            if (!DualCharacterState.Enabled || __result == null) return;
            var original = __result;
            __result = original.ContinueWith(
                t =>
                {
                    var ex = t.Exception?.GetBaseException();
                    GD.Print($"[Foreve][Dual][KillProbe] Kill 任务异常被兜底吞掉（防卡死）: {ex}");
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual][KillProbe] Kill 兜底异常: {e.Message}");
        }
    }

    /// <summary>等待原 Kill 任务完成后吞掉异常（防卡死）：异常只打日志不沿 await 链传播。</summary>
    private static async Task WrapKillTask(Task original)
    {
        try { await original; }
        catch (Exception e) { GD.Print($"[Foreve][Dual] Kill 兜底吞异常(防卡死): {e.GetType().Name}: {e.Message}"); }
    }

    // ── 5) 战斗奖励/宝箱只出 1 份 ──────────────────────────────────────────

    private static void InstallRewardPatches(Harmony harmony)
    {
        var offerD49 = typeof(CombatRoom).GetNestedType("<OfferRoomEndRewards>d__49", BindingFlags.NonPublic);
        var offerMoveNext = offerD49?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
        harmony.Patch(offerMoveNext, transpiler: new HarmonyMethod(GetMethod(nameof(OfferRoomEndRewardsTranspiler))));

        var extraD10 = typeof(TreasureRoom).GetNestedType("<DoExtraRewardsIfNeeded>d__10", BindingFlags.NonPublic);
        var extraMoveNext = extraD10?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
        harmony.Patch(extraMoveNext, transpiler: new HarmonyMethod(GetMethod(nameof(ExtraRewardsTranspiler))));

        _logger?.Info($"[Foreve][Dual] 奖励 patch 已装 (OfferRoomEndRewards.d49={offerMoveNext != null}, DoExtraRewardsIfNeeded.d10={extraMoveNext != null})");
    }

    /// <summary>OfferRoomEndRewards d__49：把循环源 combatState.get_Players() 换成 GetRewardPlayers（只主玩家）。</summary>
    private static IEnumerable<CodeInstruction> OfferRoomEndRewardsTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var helper = AccessTools.Method(typeof(DualCharacterTargeting), nameof(DualCharacterTargeting.GetRewardPlayers),
            new[] { typeof(CombatState) });
        if (helper == null) return list;

        var patched = false;
        for (var i = 0; i < list.Count; i++)
        {
            var ci = list[i];
            if (ci.opcode != OpCodes.Callvirt || ci.operand is not MethodInfo mi
                || mi.Name != "get_Players" || mi.DeclaringType != typeof(CombatState))
                continue;

            var replacement = new CodeInstruction(OpCodes.Call, helper);
            replacement.labels.AddRange(ci.labels);
            list[i] = replacement;
            patched = true;
            break;
        }

        if (patched) _logger?.Info("[Foreve][Dual] OfferRoomEndRewards 玩家循环已收敛为主玩家 (奖励1份)");
        else _logger?.Warn("[Foreve][Dual] OfferRoomEndRewards get_Players 未找到 - 游戏结构变更?");
        return list;
    }

    /// <summary>TreasureRoom.DoExtraRewardsIfNeeded d__10：把循环源 _runState.get_Players() 换成 GetRewardPlayers。</summary>
    private static IEnumerable<CodeInstruction> ExtraRewardsTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var helper = AccessTools.Method(typeof(DualCharacterTargeting), nameof(DualCharacterTargeting.GetRewardPlayers),
            new[] { typeof(IRunState) });
        if (helper == null) return list;

        var patched = false;
        for (var i = 0; i < list.Count; i++)
        {
            var ci = list[i];
            if (ci.opcode != OpCodes.Callvirt || ci.operand is not MethodInfo mi
                || mi.Name != "get_Players" || mi.DeclaringType != typeof(IPlayerCollection))
                continue;

            var replacement = new CodeInstruction(OpCodes.Call, helper);
            replacement.labels.AddRange(ci.labels);
            list[i] = replacement;
            patched = true;
            break;
        }

        if (patched) _logger?.Info("[Foreve][Dual] TreasureRoom 额外奖励玩家循环已收敛为主玩家 (奖励1份)");
        else _logger?.Warn("[Foreve][Dual] DoExtraRewardsIfNeeded get_Players 未找到 - 游戏结构变更?");
        return list;
    }

    // ── 7) 不开多人缩放（MultiplayerScalingModel 三处消费点） ───────────────

    private static void InstallScalingPatches(Harmony harmony)
    {
        var scaleHp = AccessTools.Method(typeof(Creature), "ScaleHpForMultiplayer",
            new[] { typeof(decimal), typeof(EncounterModel), typeof(int), typeof(int) });
        harmony.Patch(scaleHp, prefix: new HarmonyMethod(GetMethod(nameof(ScaleHpForMultiplayerPrefix))));

        var modifyBlock = AccessTools.Method(typeof(MultiplayerScalingModel), "ModifyBlockMultiplicative",
            new[] { typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardModel), typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay) });
        harmony.Patch(modifyBlock, prefix: new HarmonyMethod(GetMethod(nameof(ModifyBlockMultiplicativePrefix))));

        var applyD2 = typeof(PowerCmd).GetNestedType("<Apply>d__2", BindingFlags.NonPublic);
        var applyMoveNext = applyD2?.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
        harmony.Patch(applyMoveNext, transpiler: new HarmonyMethod(GetMethod(nameof(PowerApplyTranspiler))));

        _logger?.Info($"[Foreve][Dual] 缩放关闭 patch 已装 (ScaleHpForMultiplayer={scaleHp != null}, ModifyBlockMultiplicative={modifyBlock != null}, PowerCmd.Apply.d2={applyMoveNext != null})");
    }

    /// <summary>怪物 HP 多人缩放：双人模式返回原 HP（playerCount>1 时才可能缩放）。</summary>
    private static bool ScaleHpForMultiplayerPrefix(ref decimal __result, decimal hp, EncounterModel encounter, int playerCount, int actIndex)
    {
        if (DualCharacterState.Enabled && playerCount > 1)
        {
            __result = hp;
            return false;
        }
        return true;
    }

    /// <summary>敌人格挡多人缩放 hook：双人模式返回 1（不缩放）。</summary>
    private static bool ModifyBlockMultiplicativePrefix(ref decimal __result)
    {
        if (DualCharacterState.Enabled)
        {
            __result = 1m;
            return false;
        }
        return true;
    }

    /// <summary>
    /// PowerCmd.&lt;Apply&gt;d__2.MoveNext（IL 1839408）：把
    /// callvirt PowerModel::GetScaledAmountForMultiplayer 替换为
    /// call GetScaledAmountForMultiplayerSafe（双人模式返回未缩放数值）。
    /// </summary>
    private static IEnumerable<CodeInstruction> PowerApplyTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var helper = AccessTools.Method(typeof(DualCharacterTargeting), nameof(DualCharacterTargeting.GetScaledAmountForMultiplayerSafe));
        if (helper == null) return list;

        var patched = false;
        for (var i = 0; i < list.Count; i++)
        {
            var ci = list[i];
            if (ci.opcode != OpCodes.Callvirt || ci.operand is not MethodInfo mi
                || mi.Name != "GetScaledAmountForMultiplayer" || mi.DeclaringType != typeof(PowerModel))
                continue;

            var replacement = new CodeInstruction(OpCodes.Call, helper);
            replacement.labels.AddRange(ci.labels);
            list[i] = replacement;
            patched = true;
            break;
        }

        if (patched) _logger?.Info("[Foreve][Dual] PowerCmd.Apply 多人缩放已替换为双人模式不缩放");
        else _logger?.Warn("[Foreve][Dual] PowerCmd.Apply GetScaledAmountForMultiplayer 未找到 - 游戏结构变更?");
        return list;
    }

    // ── 工具 ──────────────────────────────────────────────────────────────

    private static MethodInfo GetMethod(string name)
        => typeof(DualCharacterCombatPatches).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    private static bool ContainsPlayer(IEnumerable<Player> players, Player main)
    {
        foreach (var p in players)
        {
            if (ReferenceEquals(p, main)) return true;
        }
        return false;
    }
}
