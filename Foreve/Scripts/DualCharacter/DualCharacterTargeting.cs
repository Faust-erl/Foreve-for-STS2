using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Foreve.Scripts.DualCharacter;

/// <summary>
/// 双角色模式：战斗目标/奖励/缩放辅助（批次 1b，2026-08-14 实测修复轮改造）。
///
/// 职责：
///   1. 敌人随机单目标缓存：意图显示时（NCreature.UpdateIntent 前缀）为每只怪物
///      掷一次加权随机目标（伤害×1/防御×2/辅助×0.8，权重来自 DualCharacterState.GetTauntWeight），
///      写入 <see cref="TargetsByMonster"/>；怪物实际执行招式时（MoveState.PerformMove
///      壳方法 Prefix，DualCharacterCombatPatches）走 <see cref="RollTargetForCombat"/>
///      现掷（MoveState 无怪物引用，读不到怪物缓存；与意图显示同 RNG 不同时机，可接受）。
///   2. 战斗奖励/宝箱只出 1 份：<see cref="GetRewardPlayers(CombatState)"/> /
///      <see cref="GetRewardPlayers(IRunState)"/> 把按玩家循环的奖励生成源过滤成主玩家。
///   3. 不开多人缩放：<see cref="GetScaledAmountForMultiplayerSafe"/> 在双人模式下返回
///      原始数值（PowerCmd.Apply 的 transpiler 调用点）。
///   4. 精英/Boss 战敌人 debuff 招式 AOE（批次 2b-2 + 2026-08-14 改造）：
///      <see cref="IsDebuffAoeMove(IReadOnlyList{AbstractIntent}, ICombatState)"/> 是唯一判定核心
///      （双人 + 精英/Boss 房间 + 意图含 Debuff/CardDebuff/Status 且不含
///      Attack/Defend/Heal/Buff/Stun/Summon/Escape）；意图显示（<see cref="GetOrRollTarget"/>，
///      经 MonsterModel 重载取 NextMove.Intents）与动作执行（MoveState.PerformMove 前缀，
///      经 moveState.Intents）共用同一核心 → 意图显示 == 实际命中一致。
///      AOE 时 GetOrRollTarget 返回 null（意图显示保留全量 targets 不改写缓存）、
///      动作执行不改 targets（两名全中）；攻击招式仍随机单目标。
///
/// 缓存清理：由 DualCharacterCombatPatches.Install 订阅 RitsuLib 生命周期事件
/// （SideTurnStartingEvent(Player 侧)/CombatEndedEvent/CombatStartingEvent）调用
/// <see cref="ClearTargetCache"/>。游戏单线程，无需锁。
/// </summary>
public static class DualCharacterTargeting
{
    /// <summary>每只怪物（Creature 引用为 key）本回合的随机单目标（玩家 creature）。</summary>
    private static readonly Dictionary<Creature, Creature> TargetsByMonster = new();

    /// <summary>清空目标缓存（玩家回合开始 / 战斗结束 / 战斗开始时调用）。</summary>
    public static void ClearTargetCache() => TargetsByMonster.Clear();

    /// <summary>
    /// 取/掷某怪物的本回合目标：优先读缓存（意图显示时已掷好），缓存缺失则现掷并写入。
    /// 返回 null 表示无法决定目标（调用方回退原全量列表）。
    /// </summary>
    public static Creature? GetOrRollTarget(Creature monster, ICombatState combatState)
    {
        if (monster == null || combatState == null) return null;

        // 精英/Boss 战 debuff 招式（AOE）：不掷目标、不写缓存；调用方保留全量玩家列表（两名全中）
        if (IsDebuffAoeMove(monster.Monster, combatState)) return null;

        if (TargetsByMonster.TryGetValue(monster, out var cached) && IsValidTarget(cached, combatState))
            return cached;

        var rolled = RollTarget(combatState);
        if (rolled != null)
            TargetsByMonster[monster] = rolled;
        return rolled;
    }

    /// <summary>
    /// 动作执行时的单目标现掷（MoveState.PerformMove 壳方法 Prefix 调用）：
    /// 不经缓存（MoveState 无怪物引用，无法按怪物读缓存），与意图显示的掷法同一函数
    /// （同 RNG 不同时机，目标可能与意图显示不同——可接受，见批次说明）。
    /// 返回 null 时调用方放行原 targets。
    /// </summary>
    public static Creature? RollTargetForCombat(ICombatState combatState)
    {
        if (combatState == null || !DualCharacterState.IsDualMode(combatState)) return null;
        return RollTarget(combatState);
    }

    /// <summary>
    /// 读缓存目标（Damage 层按 dealer 怪物查——意图显示时已掷好 → 意图显示==实际命中；
    /// 2026-08-14 意图头像需求：意图显示（NCreature.UpdateIntent 扫描 Prefix）掷目标写缓存，
    /// Damage 收敛（DamageMultiPrefix）读同一缓存）。
    /// </summary>
    public static Creature? GetCachedTarget(MonsterModel monster, ICombatState combatState)
    {
        if (monster?.Creature == null || combatState == null) return null;
        if (TargetsByMonster.TryGetValue(monster.Creature, out var cached) && IsValidTarget(cached, combatState))
            return cached;
        return null;
    }

    /// <summary>目标是否仍有效（存活且属于当前战斗）。</summary>
    private static bool IsValidTarget(Creature? target, ICombatState combatState)
        => target != null && !target.IsDead && ReferenceEquals(target.CombatState, combatState);

    /// <summary>
    /// 加权随机单目标：权重 = GetTauntWeight(player.Character)
    /// （mod 角色：Attack=1.0 / Defense=2.0 / Support=0.8；非 mod 角色=1.0），
    /// 已死亡玩家不参与掷取（双人：死亡角色不再被选为目标；全部死亡返回 null）。
    /// RNG 用 RunState.Rng.CombatTargets（战斗目标 RNG，与攻击判定同源）。
    /// </summary>
    private static Creature? RollTarget(ICombatState combatState)
    {
        var players = combatState?.Players;
        if (players == null || players.Count == 0) return null;

        double total = 0;
        var aliveCount = 0;
        for (var i = 0; i < players.Count; i++)
        {
            var creature = players[i].Creature;
            if (creature == null || creature.IsDead) continue;
            aliveCount++;
            total += DualCharacterState.GetTauntWeight(players[i].Character);
        }
        if (aliveCount == 0) return null;

        double roll;
        var rng = combatState?.RunState?.Rng?.CombatTargets;
        if (rng != null) roll = rng.NextDouble() * total;
        else roll = new Random().NextDouble() * total; // RNG 不可用时兜底（不应发生）

        double acc = 0;
        for (var i = 0; i < players.Count; i++)
        {
            var creature = players[i].Creature;
            if (creature == null || creature.IsDead) continue;
            acc += DualCharacterState.GetTauntWeight(players[i].Character);
            if (roll < acc) return creature;
        }
        // 浮点兜底：返回最后一个存活玩家
        for (var i = players.Count - 1; i >= 0; i--)
        {
            var creature = players[i].Creature;
            if (creature != null && !creature.IsDead) return creature;
        }
        return null;
    }

    /// <summary>
    /// 精英/Boss 战敌人 debuff 招式是否 AOE（两名角色全中，批次 2b-2 + 2026-08-14 改造）：
    /// 双人模式 &amp;&amp; 当前房间是精英/Boss &amp;&amp; 当前招式意图含玩家负面效果
    /// （DebuffIntent / CardDebuffIntent / StatusIntent）且不含任何攻击/防御/治疗/增益/眩晕/
    /// 召唤/逃脱意图（含这些 → 保持随机单目标，防止招式主体被误 AOE）。
    /// 意图显示（GetOrRollTarget 经 MonsterModel.NextMove.Intents）与动作执行
    /// （MoveState.PerformMove 前缀经 moveState.Intents）共用本核心；同一回合内
    /// NextMove 与被执行 MoveState 是同一实例 → 两次判定结果一致 → 意图显示 == 实际命中。
    /// </summary>
    public static bool IsDebuffAoeMove(IReadOnlyList<AbstractIntent> intents, ICombatState combatState)
    {
        if (intents == null || intents.Count == 0) return false;
        if (!DualCharacterState.IsDualMode(combatState)) return false;
        if (!IsEliteOrBossRoom(combatState)) return false;

        var hasDebuff = false;
        foreach (var intent in intents)
        {
            if (intent == null) continue;
            if (intent is AttackIntent or DefendIntent or HealIntent or BuffIntent or StunIntent or SummonIntent or EscapeIntent)
                return false; // 含攻击/防御/治疗/增益/眩晕/召唤/逃脱的混合招式不 AOE（保持随机单目标）
            if (intent is DebuffIntent or CardDebuffIntent or StatusIntent) hasDebuff = true;
        }
        return hasDebuff;
    }

    /// <summary>MonsterModel 版 AOE 判定（意图显示侧调用）：取 NextMove.Intents 转调核心判定。</summary>
    public static bool IsDebuffAoeMove(MonsterModel monster, ICombatState combatState)
    {
        if (monster == null) return false;
        return IsDebuffAoeMove(monster.NextMove?.Intents, combatState);
    }

    /// <summary>
    /// 洗入牌库类意图判定（轮 8）：意图列表含「向玩家手牌/卡组/弃牌堆加牌」意图
    /// （CardDebuffIntent/StatusIntent）且不含攻击/防御/治疗/增益/眩晕/召唤/逃脱
    /// （含这些 → 招式主体是其他行为，不算纯洗入类，保持随机单目标）。
    /// 双人模式下洗入类意图不受随机单目标系统影响，目标强制为主玩家
    /// （洗入副玩家空壳牌库的牌会丢，2026-08-14 用户实测）。
    /// </summary>
    public static bool IsShuffleInIntent(IReadOnlyList<AbstractIntent> intents)
    {
        if (intents == null || intents.Count == 0) return false;
        var hasShuffleIn = false;
        foreach (var intent in intents)
        {
            if (intent == null) continue;
            if (intent is AttackIntent or DefendIntent or HealIntent or BuffIntent or StunIntent or SummonIntent or EscapeIntent)
                return false; // 混合招式不强制主玩家
            if (intent is CardDebuffIntent or StatusIntent) hasShuffleIn = true;
        }
        return hasShuffleIn;
    }

    /// <summary>MonsterModel 版洗入类判定（意图显示侧调用）：取 NextMove.Intents 转调核心判定。</summary>
    public static bool IsShuffleInIntent(MonsterModel monster)
    {
        if (monster == null) return false;
        return IsShuffleInIntent(monster.NextMove?.Intents);
    }

    /// <summary>供钥令等系统复用的公开入口：当前战斗是否精英/Boss 战。</summary>
    public static bool IsEliteOrBossCombat(ICombatState combatState) => IsEliteOrBossRoom(combatState);

    /// <summary>
    /// 当前战斗房间是否精英/Boss：ICombatState.Encounter.RoomType（CombatRoom.get_RoomType
    /// IL 195011 委托 Encounter.get_RoomType），兜底 RunState.CurrentRoom as CombatRoom 链。
    /// </summary>
    private static bool IsEliteOrBossRoom(ICombatState combatState)
    {
        try
        {
            var roomType = combatState.Encounter?.RoomType;
            if (roomType == RoomType.Elite || roomType == RoomType.Boss) return true;

            var room = combatState.RunState?.CurrentRoom as CombatRoom;
            if (room != null)
            {
                roomType = room.RoomType;
                if (roomType == RoomType.Elite || roomType == RoomType.Boss) return true;
            }
        }
        catch
        {
            // 房间解析失败按非精英/Boss 处理（退化为随机单目标，最保守）
        }
        return false;
    }

    /// <summary>奖励玩家源（CombatRoom.OfferRoomEndRewards d__49 transpiler）：双人模式只出主玩家一份。</summary>
    public static IReadOnlyList<Player> GetRewardPlayers(CombatState combatState)
    {
        if (combatState == null) return Array.Empty<Player>();
        if (DualCharacterState.IsDualMode(combatState) && combatState.Players.Count > 1)
            return new[] { DualCharacterState.MainPlayer ?? combatState.Players[0] };
        return combatState.Players;
    }

    /// <summary>奖励玩家源（TreasureRoom.DoExtraRewardsIfNeeded d__10 transpiler）：同上。</summary>
    public static IReadOnlyList<Player> GetRewardPlayers(IRunState runState)
    {
        var players = runState?.Players;
        if (players == null || players.Count == 0) return Array.Empty<Player>();
        if (DualCharacterState.Enabled && players.Count > 1)
            return new[] { DualCharacterState.MainPlayer ?? players[0] };
        return players;
    }

    /// <summary>
    /// 多人缩放数值兜底（PowerCmd.Apply d__2 transpiler 把 GetScaledAmountForMultiplayer
    /// 的虚调用替换为本方法）：双人模式返回未缩放数值（不开多人缩放），
    /// 其余情况转调原虚方法（保持虚拟分派语义）。
    /// </summary>
    public static decimal GetScaledAmountForMultiplayerSafe(
        PowerModel power, ICombatState combatState, Creature applier, decimal amount, Creature target, CardModel cardSource)
    {
        if (DualCharacterState.IsDualMode(combatState)) return amount;
        return power.GetScaledAmountForMultiplayer(combatState, applier, amount, target, cardSource);
    }
}
