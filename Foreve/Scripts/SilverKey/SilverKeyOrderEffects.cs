using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using Foreve.Scripts.Content.Cards.Dore;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.SilverKey;

/// <summary>
/// 钥令 K01-K15 的效果执行器。所有「给予一名己方角色」类效果复用
/// DualCharacterChoiceUi 的头像选择弹窗（与指向性卡牌同款）。
/// </summary>
public static class SilverKeyOrderEffects
{
    private static readonly PlayerChoiceContext ChoiceContext = new ThrowingPlayerChoiceContext();

    public static async Task ExecuteAsync(SilverKeyOrderDefinition order, Player actingPlayer)
    {
        if (order == null || actingPlayer?.Creature?.CombatState == null)
        {
            GD.Print("[Foreve][SilverKey] 钥令执行失败：缺少执行上下文");
            return;
        }

        var combat = actingPlayer.Creature.CombatState;
        try
        {
            switch (order.Code)
            {
                case "K01": await ExecuteK01(combat, actingPlayer); break;
                case "K02": await ExecuteK02(combat, actingPlayer); break;
                case "K03": await ExecuteK03(combat, actingPlayer); break;
                case "K04": await ExecuteK04(combat, actingPlayer); break;
                case "K05": await ExecuteK05(combat, actingPlayer); break;
                case "K06": await ExecuteK06(combat, actingPlayer); break;
                case "K07": await ExecuteK07(combat, actingPlayer); break;
                case "K08": await ExecuteK08(combat, actingPlayer); break;
                case "K09": await ExecuteK09(combat, actingPlayer); break;
                case "K10": await ExecuteK10(combat, actingPlayer); break;
                case "K11": await ExecuteK11(combat, actingPlayer); break;
                case "K12": await ExecuteK12(combat, actingPlayer); break;
                case "K13": await ExecuteK13(combat, actingPlayer); break;
                case "K14": await ExecuteK14(combat, actingPlayer); break;
                case "K15": await ExecuteK15(combat, actingPlayer); break;
                default:
                    GD.Print($"[Foreve][SilverKey] 未实现的钥令: {order.Code}");
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][SilverKey] 钥令 {order.Code} 执行异常: {ex}");
        }
    }

    // ---------- 通用辅助 ----------

    /// <summary>牌堆/能量等「玩家侧资源」在双人局解析到主玩家（资源已合并），单玩家局用执行玩家。</summary>
    private static Player ResolveResourcePlayer(ICombatState combat, Player acting)
    {
        if (!DualCharacterState.Enabled) return acting;
        var main = DualCharacterState.MainPlayer;
        return main != null && combat.Players.Contains(main) ? main : acting;
    }

    private static List<Player> AlivePlayers(ICombatState combat)
        => combat.Players.Where(p => p?.Creature is { IsDead: false }).ToList();

    private static List<Player> DeadPlayers(ICombatState combat)
        => combat.Players.Where(p => p?.Creature is { IsDead: true }).ToList();

    private static List<Creature> AliveEnemies(ICombatState combat)
        => combat.Enemies.Where(e => e is { IsDead: false }).ToList();

    /// <summary>角色头像选择（与指向性卡牌同款弹窗）；只剩一名存活角色时不再弹窗。</summary>
    private static async Task<Creature> ChooseAllyAsync(
        ICombatState combat,
        Player acting,
        string title)
    {
        var alive = AlivePlayers(combat);
        if (alive.Count == 0) throw new InvalidOperationException("没有存活的己方角色");
        var fallback = alive[0].Creature!;

        if (alive.Count == 1) return fallback;

        return await DualCharacterChoiceUi.ShowAsync(title, alive, DeadPlayers(combat), fallback);
    }

    private static Player? PlayerOf(ICombatState combat, Creature creature)
        => combat.Players.FirstOrDefault(p => ReferenceEquals(p.Creature, creature));

    // ---------- K01-K15 ----------

    private static async Task ExecuteK01(ICombatState combat, Player acting)
    {
        var player = ResolveResourcePlayer(combat, acting);
        var hand = player.PlayerCombatState!.Hand.Cards.ToList();
        var discarded = hand.Count;

        // 双人局手牌可能含副角色 owner 的生成卡：显式指定主玩家弃牌堆，避免 Add 按 owner 丢进副玩家空壳牌堆
        var discardPile = CardPile.Get(PileType.Discard, player);
        foreach (var card in hand)
            await CardPileCmd.Add(card, discardPile);

        await CardPileCmd.Draw(ChoiceContext, discarded + 1, player);
    }

    private static async Task ExecuteK02(ICombatState combat, Player acting)
    {
        var enemies = AliveEnemies(combat);
        await PowerCmd.Apply<WeakPower>(ChoiceContext, enemies, 1, acting.Creature, null, false);
        await PowerCmd.Apply<VulnerablePower>(ChoiceContext, enemies, 1, acting.Creature, null, false);
    }

    private static async Task ExecuteK03(ICombatState combat, Player acting)
    {
        var target = await ChooseAllyAsync(combat, acting, "注射守护：选择一名己方角色");
        await CreatureCmd.GainBlock(target, 10, ValueProp.Move, null, false);

        foreach (var ally in AlivePlayers(combat))
        {
            if (ally.Creature == null || ally.Creature.MaxHp <= 0) continue;
            if ((double)ally.Creature.CurrentHp / (double)ally.Creature.MaxHp <= 0.25)
                await PowerCmd.Apply<RegenPower>(ChoiceContext, ally.Creature, 2, ally.Creature, null, false);
        }
    }

    private static async Task ExecuteK04(ICombatState combat, Player acting)
    {
        var player = ResolveResourcePlayer(combat, acting);
        await PlayerCmd.GainEnergy(2, player);
    }

    private static async Task ExecuteK05(ICombatState combat, Player acting)
    {
        var target = await ChooseAllyAsync(combat, acting, "小小心愿：选择一名己方角色");
        await PowerCmd.Apply<VigorPower>(ChoiceContext, target, 6, target, null, false);
    }

    private static async Task ExecuteK06(ICombatState combat, Player acting)
    {
        var target = await ChooseAllyAsync(combat, acting, "永世执念：选择一名己方角色");
        await PowerCmd.Apply<StrengthPower>(ChoiceContext, target, 5, target, null, false);
        await PowerCmd.Apply<SilverKeyOathPower>(ChoiceContext, target, 1, target, null, false);
    }

    private static async Task ExecuteK07(ICombatState combat, Player acting)
    {
        var player = ResolveResourcePlayer(combat, acting);
        await CardPileCmd.Draw(ChoiceContext, 4, player);
    }

    private static async Task ExecuteK08(ICombatState combat, Player acting)
    {
        foreach (var ally in AlivePlayers(combat))
        {
            await PowerCmd.Apply<VigorPower>(ChoiceContext, ally.Creature!, 4, ally.Creature, null, false);
            await PowerCmd.Apply<DoomPower>(ChoiceContext, ally.Creature!, 2, ally.Creature, null, false);
        }
    }

    private static async Task ExecuteK09(ICombatState combat, Player acting)
    {
        var target = await ChooseAllyAsync(combat, acting, "最后的誓言：选择一名己方角色");

        foreach (var weak in target.Powers.OfType<WeakPower>().ToList())
            await PowerCmd.Remove(weak);

        if (!DualCharacterTargeting.IsEliteOrBossCombat(combat)) return;

        await CreatureCmd.GainBlock(target, 9, ValueProp.Move, null, false);

        foreach (var enemy in GetBackRowEnemies(combat))
            await PowerCmd.Apply<VulnerablePower>(ChoiceContext, enemy, 2, acting.Creature, null, false);
    }

    private static async Task ExecuteK10(ICombatState combat, Player acting)
    {
        var player = ResolveResourcePlayer(combat, acting);
        var drawPile = player.PlayerCombatState!.DrawPile;
        var count = Math.Min(3, drawPile.Cards.Count);
        if (count <= 0) return;

        // 把抽牌堆按能量消耗升序重排后，用正常抽牌命令抽走最上方的 3 张（触发抽牌 hook/手牌上限规则）
        DoreCardHelpers.ReorderDrawPile(drawPile, ascending: true);
        await CardPileCmd.Draw(ChoiceContext, count, player);
    }

    private static async Task ExecuteK11(ICombatState combat, Player acting)
    {
        var allies = AlivePlayers(combat);
        if (allies.Count == 0) return;
        var dealer = allies[0].Creature!;

        var counts = new int[allies.Count];
        for (var i = 0; i < 11; i++)
            counts[Random.Shared.Next(allies.Count)]++;

        for (var i = 0; i < allies.Count; i++)
        {
            var ally = allies[i].Creature!;
            if (counts[i] > 0)
                await PowerCmd.Apply<VigorPower>(ChoiceContext, ally, counts[i], ally, null, false);

            await CreatureCmd.Damage(
                ChoiceContext, ally, 1, ValueProp.Unblockable | ValueProp.Unpowered, dealer, null);
        }
    }

    private static async Task ExecuteK12(ICombatState combat, Player acting)
    {
        var player = ResolveResourcePlayer(combat, acting);
        await PlayerCmd.GainEnergy(1, player);

        var target = await ChooseAllyAsync(combat, acting, "短暂的永恒：选择一名己方角色");
        var targetPlayer = PlayerOf(combat, target) ?? acting;

        var strikeSource = FindDeckCard(combat, targetPlayer, CardTag.Strike);
        var defendSource = FindDeckCard(combat, targetPlayer, CardTag.Defend);

        if (strikeSource != null)
            await AddEternalCopyAsync(combat, player, targetPlayer, strikeSource);
        if (defendSource != null)
            await AddEternalCopyAsync(combat, player, targetPlayer, defendSource);
    }

    private static async Task AddEternalCopyAsync(
        ICombatState combat,
        Player deckOwner,
        Player targetPlayer,
        CardModel source)
    {
        if (deckOwner.RunState is not RunState runState) return;

        // 双人局所有牌都归属主玩家牌库（副本的 owner 也必须是主玩家），打出时由
        // DualCharacterCardOwnerPatch 按卡牌所属角色卡池自动把效果重定向到对应角色。
        var copy = DoreCardHelpers.CreateCardCopy(runState, source, deckOwner);
        if (copy == null)
        {
            GD.Print($"[Foreve][SilverKey] K12 复制失败: {source.Id.Entry}");
            return;
        }

        if (source.IsUpgraded && !copy.IsUpgraded)
        {
            copy.UpgradeInternal();
            copy.FinalizeUpgradeInternal();
        }

        copy.AddKeyword(CardKeyword.Ethereal);
        copy.AddKeyword(CardKeyword.Exhaust);

        await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, targetPlayer, CardPilePosition.Top);
    }

    /// <summary>在全部玩家牌库中寻找属于 targetPlayer 角色、且带指定标签的卡牌（双人局牌库已合并）。</summary>
    private static CardModel? FindDeckCard(ICombatState combat, Player targetPlayer, CardTag tag)
    {
        foreach (var player in combat.Players)
        {
            var match = player.Deck.Cards.FirstOrDefault(c =>
                c.Tags.Contains(tag) && BelongsToCharacter(c, targetPlayer));
            if (match != null) return match;
        }

        // 兜底：从角色初始牌组取对应卡型（牌库里没有时仍能生成基础版复制）。
        var starter = targetPlayer.Character?.StartingDeck?.FirstOrDefault(c => c.Tags.Contains(tag));
        return starter;
    }

    private static bool BelongsToCharacter(CardModel card, Player player)
    {
        var pool = player.Character?.CardPool;
        if (pool == null) return false;
        try
        {
            return pool.AllCardIds.Contains(card.Id);
        }
        catch
        {
            return false;
        }
    }

    private static async Task ExecuteK13(ICombatState combat, Player acting)
    {
        await PowerCmd.Apply<ThornsPower>(ChoiceContext, acting.Creature!, 4, acting.Creature, null, false);

        var attackers = AliveEnemies(combat)
            .Where(e => e.Monster?.IntendsToAttack == true)
            .ToList();

        await PowerCmd.Apply<WeakPower>(ChoiceContext, attackers, 1, acting.Creature, null, false);
    }

    private static async Task ExecuteK14(ICombatState combat, Player acting)
    {
        var player = ResolveResourcePlayer(combat, acting);
        await PlayerCmd.GainEnergy(1, player);

        var target = await ChooseAllyAsync(combat, acting, "蚀骨的拥抱：选择一名己方角色");
        await PowerCmd.Apply<SilverKeyNextTurnBlockPower>(ChoiceContext, target, 9, target, null, false);
    }

    private static async Task ExecuteK15(ICombatState combat, Player acting)
    {
        var enemies = AliveEnemies(combat);
        var highest = enemies.OrderByDescending(e => e.CurrentHp).FirstOrDefault();
        if (highest != null)
            await PowerCmd.Apply<PoisonPower>(ChoiceContext, highest, 4, acting.Creature, null, false);

        foreach (var ally in AlivePlayers(combat))
            await CreatureCmd.GainBlock(ally.Creature!, 6, ValueProp.Move, null, false);
    }

    /// <summary>
    /// 「最后一排」的敌人：优先按 Encounter.Slots 的最后一个槽位名匹配（如 NibbitsNormal 的
    /// front/back，取 back）；没有槽位定义时回退为空（不误伤）。该判定已整理进问题文档待确认。
    /// </summary>
    private static List<Creature> GetBackRowEnemies(ICombatState combat)
    {
        var result = new List<Creature>();
        try
        {
            var slots = combat.Encounter?.Slots;
            if (slots == null || slots.Count == 0) return result;

            var backSlot = slots[^1];
            result.AddRange(AliveEnemies(combat).Where(e => e.SlotName == backSlot));
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][SilverKey] 最后一排解析异常: {ex.Message}");
        }
        return result;
    }
}
