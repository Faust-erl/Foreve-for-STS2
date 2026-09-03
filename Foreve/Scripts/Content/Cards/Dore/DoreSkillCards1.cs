using System.Linq;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Dore;

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
[RegisterCharacterStarterCard(typeof(Characters.Dore.DoreCharacter), 1)]
public class DoreEquivalentExchange : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public DoreEquivalentExchange() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Basic, target: TargetType.AnyPlayer, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target ?? Owner.Creature;
        var hand = Owner.PlayerCombatState.Hand.Cards.ToList();
        var count = hand.Count;

        foreach (var card in hand)
            await CardPileCmd.Add(card, PileType.Discard);

        var random = new Random();
        var blockAmount = IsUpgraded ? 5 : 3;
        for (var i = 0; i < count; i++)
        {
            if (random.Next(2) == 0)
            {
                await CreatureCmd.GainBlock(target, blockAmount, ValueProp.Move, cardPlay, false);
            }
            else
            {
                await PowerCmd.Apply<RegenPower>(choiceContext, target, 1, target, null, false);
            }
        }
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreBeyondBodySkill : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreBeyondBodySkill() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var hand = Owner.PlayerCombatState.Hand.Cards
            .Where(c => c.Type is CardType.Status or CardType.Power)
            .ToList();
        var count = hand.Count;

        foreach (var card in hand)
            await CardPileCmd.Add(card, PileType.Exhaust);

        if (count > 0)
            await CardPileCmd.Draw(choiceContext, count, Owner);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreCalibrate : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [];

    public DoreCalibrate() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Common, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target!;
        await PowerCmd.Apply<VulnerablePower>(choiceContext, target, 2, Owner.Creature, null, false);

        var debuffs = Owner.Creature.Powers
            .Where(p => p.Type == PowerType.Debuff)
            .OrderBy(p => p.Amount)
            .ToList();
        if (debuffs.Count > 0)
            await PowerCmd.Remove(debuffs[0]);
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreFragileOrgans : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreFragileOrgans() : base(baseCost: 0, type: CardType.Skill, rarity: CardRarity.Common, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<VulnerablePower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
        await CardPileCmd.Draw(choiceContext, IsUpgraded ? 3 : 2, Owner);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreAwaken : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreAwaken() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Common, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<VigorPower>(choiceContext, Owner.Creature, 4, Owner.Creature, null, false);
        await CardPileCmd.Draw(choiceContext, IsUpgraded ? 2 : 1, Owner);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreIsolateDeath : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreIsolateDeath() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var candidates = Owner.PlayerCombatState.Hand.Cards
            .Where(c => IsUpgraded || c.Type == CardType.Skill)
            .ToList();
        if (candidates.Count == 0) return;

        var prefs = new CardSelectorPrefs(
            new LocString("foreve_I18N_cards", "FOREVE_CARD_SELECT_ISOLATE_DEATH_HAND"),
            Math.Min(1, candidates.Count), 1)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };

        var result = await CardSelectCmd.FromHand(
            choiceContext,
            Owner,
            prefs,
            c => IsUpgraded || c.Type == CardType.Skill,
            this);
        var picked = result.FirstOrDefault();
        if (picked == null) return;

        CardCmd.ApplyKeyword(picked, [CardKeyword.Exhaust]);
        await CardCmd.AutoPlay(choiceContext, picked, null, AutoPlayType.Default, skipXCapture: true, skipCardPileVisuals: false);
    }

    protected override void OnUpgrade()
    {
    }
}
