using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using Foreve.Scripts.Content.Cards.Dore;
using Foreve.Scripts.Content.Cards.Rotan;

namespace Foreve.Scripts.Combat;

/// <summary>
/// 战斗结束费用还原（统一机制）：
/// 桀骜之刃等在每局战斗内会改变费用的牌，统一在每局战斗结束后将费用还原：
/// 1) 静态「本回合/本局」计数清零（桀骜之刃 / 利维坦之心 / 混沌之兽）——否则战斗结束后
///    这些牌在牌库/奖励/地图等战斗外界面查询费用时，仍按上一场残留计数扣减显示；
/// 2) 战斗开始快照所有玩家卡牌的基准费用（GetWithModifiers(None) = 持久 _base），战斗结束
///    逐张还原（SetCustomBaseCost）——覆盖「本场战斗中费用-1」（突触适应）、
///    「仅在本回合费用+1」未还原的残留（格式塔复制）等用 UpgradeBy 改 _base 的实现；
/// 3) 重置卡牌自身的本场状态标记（突触适应 _costReducedThisCombat / 格式塔 _temporaryCostBonus），
///    保证下一场战斗能再次触发减费/加费。
///
/// 时序依据（游戏反编译实证）：
/// - CombatEndedEvent 在 Hook.AfterCombatEnd 完成后发布（胜利路径；纯败北 ProcessPendingLoss
///   不发布该事件——败北即本局结束，无需还原）；
/// - 事件发布时 PlayerCombatState.AfterCombatEnd()（清空战斗牌堆）尚未执行，
///   存活卡牌可能在 Deck 或战斗牌堆中，故两处都扫描。
/// </summary>
public static class CombatCostReset
{
    private static bool _installed;

    /// <summary>战斗开始时各卡牌的基准费用快照（引用相等键，CardModel 实例跨战斗复用）。</summary>
    private static readonly Dictionary<CardModel, int> CombatStartCosts = new(ReferenceEqualityComparer.Instance);

    public static void EnsureInstalled()
    {
        if (_installed) return;
        _installed = true;
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(OnCombatStarting, replayCurrentState: false);
        RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(OnCombatEnded, replayCurrentState: false);
        // 开新局清空快照，避免跨局残留卡牌引用
        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(_ => CombatStartCosts.Clear(), replayCurrentState: false);
        GD.Print("[Foreve] 战斗结束费用还原已安装 (CombatStartingEvent/CombatEndedEvent)");
    }

    /// <summary>战斗开始：清零静态计数（防御）并快照所有玩家卡牌的基准费用。</summary>
    private static void OnCombatStarting(CombatStartingEvent e)
    {
        try
        {
            // 先清零再快照：确保快照读到的是干净基准（桀骜之刃/利维坦之心/混沌之兽）
            RebelBladeStrikeTracker.ResetAfterCombat();
            LeviathanHeartStrikeTracker.ResetAfterCombat();
            ChaosBeastStrikeTracker.ResetAfterCombat();

            CombatStartCosts.Clear();
            if (e.RunState is not RunState rs) return;
            foreach (var player in rs.Players)
            {
                if (player == null) continue;
                Snapshot(player.Deck);
                var pcs = player.PlayerCombatState;
                if (pcs != null)
                {
                    foreach (var pile in pcs.AllPiles)
                        Snapshot(pile);
                }
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve] 战斗开始费用快照异常: {ex.Message}");
        }
    }

    /// <summary>战斗结束：统一还原所有在战斗中改变过费用的牌，并清零静态计数。</summary>
    private static void OnCombatEnded(CombatEndedEvent e)
    {
        try
        {
            // 1) 静态计数清零：桀骜之刃 / 利维坦之心 / 混沌之兽的跨战斗残留
            RebelBladeStrikeTracker.ResetAfterCombat();
            LeviathanHeartStrikeTracker.ResetAfterCombat();
            ChaosBeastStrikeTracker.ResetAfterCombat();

            // 2) 快照覆盖的卡：把持久费用（_base）还原到战斗开始时的基准
            foreach (var pair in CombatStartCosts)
            {
                var card = pair.Key;
                try
                {
                    var energyCost = card?.EnergyCost;
                    if (energyCost == null) continue;
                    var current = energyCost.GetWithModifiers(CostModifiers.None);
                    if (current == pair.Value) continue;
                    energyCost.SetCustomBaseCost(pair.Value);
                    GD.Print($"[Foreve] 战斗结束费用还原: {card.GetType().Name} {current} -> {pair.Value}");
                }
                catch (Exception ex)
                {
                    GD.Print($"[Foreve] 战斗结束费用还原单卡异常: {card?.GetType().Name}: {ex.Message}");
                }
            }
            CombatStartCosts.Clear();

            // 3) 卡牌自身本场状态重置（突触适应减费标记 / 格式塔回合加费残留）。
            //    必须在快照还原之后执行：格式塔复制牌不在战斗开始快照内，由本步撤销其 UpgradeBy 加费。
            if (e.RunState is RunState rs)
            {
                foreach (var player in rs.Players)
                {
                    if (player == null) continue;
                    ResetCardState(player.Deck);
                    var pcs = player.PlayerCombatState;
                    if (pcs != null)
                    {
                        foreach (var pile in pcs.AllPiles)
                            ResetCardState(pile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve] 战斗结束费用还原异常: {ex.Message}");
        }
    }

    private static void Snapshot(CardPile? pile)
    {
        if (pile == null) return;
        foreach (var card in pile.Cards)
        {
            if (card?.EnergyCost == null) continue;
            CombatStartCosts.TryAdd(card, card.EnergyCost.GetWithModifiers(CostModifiers.None));
        }
    }

    private static void ResetCardState(CardPile? pile)
    {
        if (pile == null) return;
        foreach (var card in pile.Cards)
        {
            try
            {
                switch (card)
                {
                    case DoreSynapticAdaptation dsa:
                        dsa.ResetCombatCostState();
                        break;
                    case DoreGestalt dg:
                        dg.ResetCombatCostState();
                        break;
                }
            }
            catch (Exception ex)
            {
                GD.Print($"[Foreve] 卡牌本场状态重置异常: {card?.GetType().Name}: {ex.Message}");
            }
        }
    }
}
