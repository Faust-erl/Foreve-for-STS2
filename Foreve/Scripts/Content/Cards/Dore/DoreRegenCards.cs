using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Dore;

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreActiveProliferation : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreActiveProliferation() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Common, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.Damage(choiceContext, Owner.Creature, 2,
            ValueProp.Unblockable | ValueProp.Unpowered, null, null);

        await PowerCmd.Apply<RegenPower>(choiceContext, Owner.Creature, 2, Owner.Creature, null, false);
        await CardPileCmd.Draw(choiceContext, 1, Owner);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreShellResonance : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreShellResonance() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Common, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var hadRegen = Owner.Creature.Powers.OfType<RegenPower>().Any(p => p.Amount > 0);

        if (hadRegen)
        {
            await CreatureCmd.GainBlock(Owner.Creature, IsUpgraded ? 10 : 8, ValueProp.Move, cardPlay, false);
        }
        else
        {
            await PowerCmd.Apply<RegenPower>(choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null, false);
        }
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreSeveredLimbHeal : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public DoreSeveredLimbHeal() : base(baseCost: 2, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var amount = IsUpgraded ? 4 : 3;

        await CreatureCmd.Heal(Owner.Creature, amount, false);
        await PowerCmd.Apply<RegenPower>(choiceContext, Owner.Creature, amount, Owner.Creature, null, false);
        await PowerCmd.Apply<VigorPower>(choiceContext, Owner.Creature, amount, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
    }
}
