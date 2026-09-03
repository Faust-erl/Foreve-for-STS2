using System.Linq;
using Godot;
using STS2RitsuLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using Foreve.Scripts.Characters;

namespace Foreve.Scripts.Combat;

/// <summary>
/// 打牌动作动画控制器：订阅 RitsuLib CardPlayingEvent（出牌瞬间，牌结算前发布），
/// 找到出牌玩家对应的战斗立绘（ForeveCreatureVisualsBase），按卡牌类型播放动作动画，
/// 播完由立绘自动回待机。
///
/// 触发机制依据：RitsuLib src/Lifecycle/Patches/CombatHookLifecyclePatches.cs
/// BeforeCardPlayedLifecyclePatch（Hook.BeforeCardPlayed Prefix -> CardPlayingEvent，每张牌必发）。
/// 原版角色动画走 Spine（NCreatureVisuals.IsSpineNode 分支），mod 用 AnimatedSprite2D
/// 无法走游戏动画链路，故用生命周期事件自行播放。
///
/// 一对多动画映射（2026-08 用户要求，素材=角色动画 GIF：打击/防御/技能1/技能2/觉醒/爆发/受击）：
///   打击标签（CardTag.Strike）        -> "attack"  （打击动画）
///   防御标签（CardTag.Defend）        -> "defense"（防御动画）
///   攻击牌（CardType.Attack）         -> "attack"
///   能力牌（CardType.Power）          -> "awaken" （觉醒动画）
///   技能牌（CardType.Skill）：
///     含「消耗」关键字（Exhaust）     -> "burst"  （一次性大技能/爆发类）
///     其他                            -> "skill1" （技能1动画）
///   诅咒/状态牌                       -> 不播
///   目标角色没有对应动画时（如萝坦无觉醒）由立绘基类回退 "defense"。
/// CardModel 无公开 CardType 属性（IL 实证：私有自动属性 '<Type>k__BackingField'），
/// 用反射读取并缓存（按具体卡类型缓存，每类卡只反射一次）。
/// </summary>
public static class PlayerCardAnimationController
{
    private static bool _installed;

    /// <summary>在 Entry.Init 调用（幂等）：订阅打牌事件。</summary>
    public static void EnsureInstalled()
    {
        if (_installed) return;
        _installed = true;
        // 瞬时事件：replayCurrentState 必须 false，避免补发历史事件误播动画
        RitsuLibFramework.SubscribeLifecycle<CardPlayingEvent>(OnCardPlaying, replayCurrentState: false);
        GD.Print("[Foreve] 打牌动作动画控制器已安装 (CardPlayingEvent)");
    }

    private static void OnCardPlaying(CardPlayingEvent e)
    {
        try
        {
            var card = e.CardPlay?.Card;
            if (card == null)
            {
                GD.Print("[Foreve] 打牌动画: CardPlay.Card 为 null，跳过");
                return;
            }
            if (card.Owner == null)
            {
                GD.Print($"[Foreve] 打牌动画: {card.GetType().Name} 的 Owner 为 null（非玩家牌），跳过");
                return;
            }

            var room = NCombatRoom.Instance;
            if (room == null)
            {
                GD.Print("[Foreve] 打牌动画: NCombatRoom.Instance 为 null，跳过");
                return;
            }

            // 由玩家实体找到对应的战斗立绘节点
            NCreature? creatureNode = null;
            var nodeCount = 0;
            foreach (var node in room.CreatureNodes)
            {
                nodeCount++;
                if (node?.Entity != null && ReferenceEquals(node.Entity, card.Owner.Creature))
                {
                    creatureNode = node;
                    break;
                }
            }
            if (creatureNode == null)
            {
                GD.Print($"[Foreve] 打牌动画: 未匹配到 {card.Owner} 的立绘节点（CreatureNodes 共 {nodeCount} 个），跳过");
                return;
            }

            if (creatureNode.Visuals is not ForeveCreatureVisualsBase visuals)
            {
                GD.Print($"[Foreve] 打牌动画: {creatureNode.Name} 的 Visuals 类型不匹配（实际 {creatureNode.Visuals?.GetType().Name ?? "null"}，期望 ForeveCreatureVisualsBase），跳过");
                return;
            }

            var animName = GetActionAnimation(card);
            if (string.IsNullOrEmpty(animName))
            {
                GD.Print($"[Foreve] 打牌动画: {card.GetType().Name} 类型={GetCardType(card)} 无映射动画（诅咒/状态牌），跳过");
                return;
            }
            // optimizer：移除每次出牌必打的成功日志（含 Tags 遍历拼接，纯调试开销）
            visuals.PlayActionAnimation(animName);
        }
        catch (System.Exception ex)
        {
            // 动画失败不影响战斗流程，仅记日志
            GD.Print($"[Foreve] 打牌动画播放异常: {ex}");
        }
    }

    /// <summary>按卡牌类型映射动作动画名（无动画返回空串）。</summary>
    private static string GetActionAnimation(CardModel card)
    {
        // 标签优先（游戏的公开信号）
        if (card.Tags.Contains(CardTag.Strike)) return "attack";
        if (card.Tags.Contains(CardTag.Defend)) return "defense";

        // CardModel 无公开 Type 属性 -> 反射私有自动属性（IL 实证 '<Type>k__BackingField'）
        var type = GetCardType(card);
        switch (type)
        {
            case CardType.Attack:
                return "attack";
            case CardType.Power:
                return "awaken";
            case CardType.Skill:
                // 消耗类（Exhaust）一次性强力技能 -> 爆发动画；普通技能 -> 技能1动画
                return card.Keywords.Contains(CardKeyword.Exhaust) ? "burst" : "skill1";
            case CardType.Curse:
            case CardType.Status:
                return ""; // 诅咒/状态牌不播动作
            default:
                return "skill1";
        }
    }

    // ---- 反射读取 CardModel.Type（私有属性，按卡类型缓存）----
    private static System.Reflection.PropertyInfo? _typeProperty;
    private static readonly System.Collections.Generic.Dictionary<System.Type, CardType> _cardTypeCache = new();

    private static CardType GetCardType(CardModel card)
    {
        var concreteType = card.GetType();
        if (_cardTypeCache.TryGetValue(concreteType, out var cached)) return cached;
        try
        {
            _typeProperty ??= typeof(CardModel).GetProperty(
                "Type",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var value = _typeProperty?.GetValue(card);
            var type = value is CardType ct ? ct : CardType.None;
            _cardTypeCache[concreteType] = type;
            return type;
        }
        catch (System.Exception ex)
        {
            GD.Print($"[Foreve] 读取 CardModel.Type 失败: {ex.Message}");
            return CardType.None;
        }
    }
}
