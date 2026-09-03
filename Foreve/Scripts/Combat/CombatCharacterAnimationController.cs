using Godot;
using STS2RitsuLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using Foreve.Scripts.Characters;

namespace Foreve.Scripts.Combat;

/// <summary>
/// 受击/通关动画控制器：订阅 RitsuLib 生命值变化与战斗胜利事件，
/// 找到对应玩家角色的战斗立绘（ForeveCreatureVisualsBase），播放 hurt/victory 动画，
/// 播完由立绘自动回待机。
///
/// 触发机制依据：
///   - CurrentHpChangedEvent（每次生物血量变化发布）：Delta &lt; 0 表示掉血，
///     且目标 Creature 属于我方玩家（RunState.Players 任一玩家的 Creature 引用相等）时播 "hurt"。
///   - CombatVictoryEvent（战斗胜利发布）：对每个玩家角色播 "victory"。
/// 立绘节点查找方式与 PlayerCardAnimationController 一致：
///   NCombatRoom.Instance.CreatureNodes 中 node.Entity 与目标 Creature ReferenceEquals 的节点。
/// 动画失败不影响战斗流程，所有异常仅记日志。
/// </summary>
public static class CombatCharacterAnimationController
{
    private static bool _installed;

    /// <summary>在 Entry.Init 调用（幂等）：订阅受击与通关事件。</summary>
    public static void EnsureInstalled()
    {
        if (_installed) return;
        _installed = true;
        // 瞬时事件：replayCurrentState 必须 false，避免补发历史事件误播动画
        RitsuLibFramework.SubscribeLifecycle<CurrentHpChangedEvent>(OnCurrentHpChanged, replayCurrentState: false);
        RitsuLibFramework.SubscribeLifecycle<CombatVictoryEvent>(OnCombatVictory, replayCurrentState: false);
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(OnCombatStarting, replayCurrentState: false);
        GD.Print("[Foreve] 受击/通关/死亡动画控制器已安装 (CurrentHpChangedEvent/CombatVictoryEvent/CombatStartingEvent)");
    }

    /// <summary>掉血事件：Delta&lt;0 且是我方玩家角色 -> 播放受击动画；死亡 -> 播死亡并保持；复活 -> 回待机。</summary>
    private static void OnCurrentHpChanged(CurrentHpChangedEvent e)
    {
        try
        {
            if (e.Creature == null)
            {
                return;
            }

            // 只处理我方玩家角色
            var isPlayer = false;
            foreach (var player in e.RunState.Players)
            {
                if (player?.Creature != null && ReferenceEquals(player.Creature, e.Creature))
                {
                    isPlayer = true;
                    break;
                }
            }
            if (!isPlayer)
            {
                return;
            }

            if (e.Creature.IsDead)
            {
                TryPlayDeath(e.Creature);
                return;
            }

            // 复活：死亡立绘状态 -> 回待机
            if (TryGetVisuals(e.Creature, out var visuals) && visuals.IsShowingDeath)
            {
                visuals.PlayIdle();
                return;
            }

            if (e.Delta < 0)
            {
                TryPlayAction(e.Creature, "hurt");
            }
        }
        catch (System.Exception ex)
        {
            // 动画失败不影响战斗流程，仅记日志
            GD.Print($"[Foreve] 受击/死亡动画播放异常: {ex}");
        }
    }

    /// <summary>战斗开始：已死亡的角色直接进入死亡动画最后一帧（死亡跨战斗保持）。</summary>
    private static void OnCombatStarting(CombatStartingEvent e)
    {
        try
        {
            foreach (var player in e.RunState.Players)
            {
                if (player?.Creature is { IsDead: true })
                {
                    TryPlayDeath(player.Creature);
                }
            }
        }
        catch (System.Exception ex)
        {
            GD.Print($"[Foreve] 战斗开始死亡动画播放异常: {ex}");
        }
    }

    /// <summary>战斗胜利事件：每个玩家角色播放通关动画。</summary>
    private static void OnCombatVictory(CombatVictoryEvent e)
    {
        try
        {
            foreach (var player in e.RunState.Players)
            {
                if (player?.Creature == null)
                {
                    GD.Print("[Foreve] 通关动画: 玩家 Creature 为 null，跳过");
                    continue;
                }
                if (TryPlayAction(player.Creature, "victory"))
                {
                    GD.Print($"[Foreve] 通关动画: {player.Creature} -> 播放 'victory'");
                }
            }
        }
        catch (System.Exception ex)
        {
            // 动画失败不影响战斗流程，仅记日志
            GD.Print($"[Foreve] 通关动画播放异常: {ex}");
        }
    }

    /// <summary>按 Creature 实体找到对应立绘节点。成功返回 true。</summary>
    private static bool TryGetVisuals(object creature, out ForeveCreatureVisualsBase visuals)
    {
        visuals = null!;
        var room = NCombatRoom.Instance;
        if (room == null)
        {
            GD.Print("[Foreve] 动画: NCombatRoom.Instance 为 null，跳过");
            return false;
        }

        var nodeCount = 0;
        foreach (var node in room.CreatureNodes)
        {
            nodeCount++;
            if (node?.Entity != null && ReferenceEquals(node.Entity, creature))
            {
                if (node.Visuals is ForeveCreatureVisualsBase v)
                {
                    visuals = v;
                    return true;
                }
                GD.Print($"[Foreve] 动画: {node.Name} 的 Visuals 类型不匹配（实际 {node.Visuals?.GetType().Name ?? "null"}，期望 ForeveCreatureVisualsBase），跳过");
                return false;
            }
        }
        GD.Print($"[Foreve] 动画: 未匹配到目标立绘节点（CreatureNodes 共 {nodeCount} 个），跳过");
        return false;
    }

    /// <summary>播放死亡动画并保持最后一帧。</summary>
    private static void TryPlayDeath(object creature)
    {
        if (TryGetVisuals(creature, out var visuals))
        {
            visuals.PlayDeathAnimation();
        }
    }

    /// <summary>按 Creature 实体找到对应立绘节点并播放动作动画。成功返回 true。</summary>
    private static bool TryPlayAction(object creature, string animName)
    {
        if (TryGetVisuals(creature, out var visuals))
        {
            visuals.PlayActionAnimation(animName);
            return true;
        }
        return false;
    }
}
