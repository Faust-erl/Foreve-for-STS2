using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace Foreve.Scripts.Content.Cards.Rotan;

/// <summary>
/// 桀骜之刃减费「直通」补丁（2026-08-23，实测修复）：
///
/// 背景：RitsuLib 的 capability 费用补丁（CardModelCapabilityPatches.EnergyCostPatch.Postfix）在
///   CostModifiers.Global 修饰 && 卡牌位于战斗内（_card.CombatState != null）时跳过
///   ICardEnergyCostContributor（守卫）。读档后的手牌悬停/费用显示查询恰好命中该组合 →
///   ModifyEnergyCost 零调用、悬停预览显示基础 3 费（探针实证：读档局无任何费用计算日志，
///   新开局战斗外/结算路径 CombatState==null 时反而正常）。
///
/// 本补丁与 RitsuLib 守卫正交：无论修饰符与战斗状态，桀骜之刃的费用无条件扣除
/// 本回合已打出打击数（下限 0），保证角标/悬停/可打出检查三处一致。
/// 由 Entry 的全局 PatchAll（LocTableI18NInjection 同 assembly）自动安装。
/// </summary>
[HarmonyPatch(typeof(CardEnergyCost), nameof(CardEnergyCost.GetWithModifiers), new[] { typeof(CostModifiers) })]
internal static class RotanRebelBladeCostPatch
{
    /// <summary>CardEnergyCost 内部的卡牌引用字段（RitsuLib 同字段名）。</summary>
    private static readonly FieldInfo CardField = AccessTools.Field(typeof(CardEnergyCost), "_card");

    private static void Postfix(CardEnergyCost __instance, ref int __result)
    {
        try
        {
            var card = CardField?.GetValue(__instance) as CardModel;
            if (card == null || card.GetType() != typeof(RotanRebelBlade)) return;

            var strikes = RebelBladeStrikeTracker.StrikesPlayedThisTurn;
            if (strikes <= 0) return;

            var reduced = Math.Max(0, __result - strikes);
            if (reduced == __result) return;
            __result = reduced;
        }
        catch
        {
            // 防御：任何异常不得影响正常费用查询，静默返回原费用
        }
    }
}