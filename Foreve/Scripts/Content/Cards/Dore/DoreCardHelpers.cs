using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace Foreve.Scripts.Content.Cards.Dore;

/// <summary>朵尔卡牌共用工具：牌堆排序、费用读取、卡牌复制。</summary>
public static class DoreCardHelpers
{
    /// <summary>
    /// 排序用费用：通过 CardEnergyCost.GetWithModifiers 反射读取当前费用；
    /// X 费（返回 -1）按 0 处理，其余取非负整数。
    /// </summary>
    public static int GetSortCost(CardModel card)
    {
        try
        {
            var cost = card.EnergyCost;
            var method = typeof(CardEnergyCost).GetMethod(
                "GetWithModifiers",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) return 0;

            var parameterType = method.GetParameters()[0].ParameterType;
            var modifiers = Activator.CreateInstance(parameterType);
            var value = method.Invoke(cost, new object?[] { modifiers });
            var numeric = Convert.ToInt32(value);
            return Math.Max(0, numeric);
        }
        catch
        {
            return 0;
        }
    }

    public static bool IsSortedAscending(IEnumerable<CardModel> cards)
    {
        int? previous = null;
        foreach (var card in cards)
        {
            var cost = GetSortCost(card);
            if (previous.HasValue && previous.Value > cost)
                return false;
            previous = cost;
        }
        return true;
    }

    /// <summary>把抽牌堆按费用重新排序（升序=低费在上，降序=高费在上）。</summary>
    public static void ReorderDrawPile(CardPile drawPile, bool ascending)
    {
        if (drawPile == null || drawPile.Cards.Count <= 1) return;

        var ordered = ascending
            ? drawPile.Cards.OrderBy(GetSortCost).ToList()
            : drawPile.Cards.OrderByDescending(GetSortCost).ToList();

        drawPile.Clear(false);
        foreach (var card in ordered)
            drawPile.AddInternal(card, -1, false);
    }

    /// <summary>
    /// 安全重排抽牌堆：用高层 CardPileCmd 以弃牌堆作临时中转，避免低层 Clear/AddInternal
    /// 导致牌堆外卡牌（尤其是手牌）被误判为消耗。只移动抽牌堆里的牌，不影响弃牌堆原有卡牌。
    /// </summary>
    public static async Task ReorderDrawPileSafelyAsync(
        PlayerChoiceContext choiceContext,
        CardPile drawPile,
        bool ascending)
    {
        if (drawPile == null || drawPile.Cards.Count <= 1) return;

        var drawCards = drawPile.Cards.ToList();
        var ordered = ascending
            ? drawCards.OrderBy(GetSortCost).ToList()
            : drawCards.OrderByDescending(GetSortCost).ToList();

        // 先全部移到弃牌堆中转（只移抽牌堆的牌），再按序放回抽牌堆底部（低费在上）。
        foreach (var card in drawCards)
            await CardPileCmd.Add(card, PileType.Discard, CardPilePosition.Bottom, null, true);
        foreach (var card in ordered)
            await CardPileCmd.Add(card, PileType.Draw, CardPilePosition.Bottom, null, true);
    }

    /// <summary>
    /// 洗牌抽牌堆并返回位置发生变化的卡牌数。
    /// 实现方式与游戏牌堆一致：快照旧顺序 → 清空 → 按新顺序 AddInternal(-1) 追加。
    /// </summary>
    public static int ShuffleDrawPileAndCountMoved(CardPile drawPile)
    {
        if (drawPile == null) return 0;

        var oldOrder = drawPile.Cards.ToList();
        if (oldOrder.Count <= 1)
        {
            // 0/1 张牌无法改变位置，但仍算完成一次洗牌。
            return 0;
        }

        var newOrder = oldOrder.ToList();
        var random = new Random();
        for (var i = newOrder.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (newOrder[i], newOrder[j]) = (newOrder[j], newOrder[i]);
        }

        drawPile.Clear(false);
        foreach (var card in newOrder)
            drawPile.AddInternal(card, -1, false);

        var moved = 0;
        for (var i = 0; i < oldOrder.Count; i++)
        {
            if (!ReferenceEquals(oldOrder[i], newOrder[i]))
                moved++;
        }
        return moved;
    }

    /// <summary>
    /// 生成一张与来源同类型的可入战卡牌（用于「获得……的复制」效果）。
    /// 战斗内必须用 CombatState.CreateCard&lt;T&gt;(Player) 创建，卡才会进入 CombatState._allCards；
    /// 否则后续 AddGeneratedCardToCombat 只入牌堆不登记战斗状态，打出/回合结束消耗会报
    /// "must be added to a CombatState before playing/adding it"。
    /// 这里用反射兼容任意来源类型；战斗外回退 RunState.CreateCard&lt;T&gt;(Player)。
    /// </summary>
    public static CardModel? CreateCardCopy(RunState runState, CardModel source, Player owner)
    {
        if (runState == null || source == null || owner == null) return null;

        try
        {
            // 优先以战斗状态为创建作用域：战斗内生成的卡必须被 CombatState 登记。
            var scope = owner.Creature?.CombatState as object ?? runState;
            var method = scope.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "CreateCard" && m.IsGenericMethodDefinition);
            if (method == null) return null;

            var typed = method.MakeGenericMethod(source.GetType());
            return typed.Invoke(scope, [owner]) as CardModel;
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Foreve][Dore] 卡牌复制失败: {source.GetType().Name}, {ex.Message}");
            return null;
        }
    }

    public static string CardPortraitPathOrFallback(string cardClassName)
    {
        var dorePath = $"res://Foreve/Assets/Cards/Dore/{cardClassName}.png";
        return ResourceLoader.Exists(dorePath)
            ? dorePath
            : "res://Foreve/Assets/Cards/Rotan/RotanStrike.png";
    }
}
