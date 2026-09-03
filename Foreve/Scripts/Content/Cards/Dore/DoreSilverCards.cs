using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.SilverKey;

namespace Foreve.Scripts.Content.Cards.Dore;

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreCoreOverload : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreCoreOverload() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Common, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var current = SecondaryResourceCmd.Get(Owner, SilverKeyResource.ResourceId);
        var spend = Math.Min(3, current);
        if (spend <= 0) return;

        await SecondaryResourceCmd.Spend(Owner, SilverKeyResource.ResourceId, spend, this, this);

        var amount = IsUpgraded ? 5 : 3;
        for (var i = 0; i < spend; i++)
        {
            await DamageCmd.Attack(amount)
                .FromCard(this)
                .Targeting(cardPlay.Target!)
                .Execute(choiceContext);

            await CreatureCmd.GainBlock(Owner.Creature, amount, ValueProp.Move, cardPlay, false);
        }
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreTurbulentConversion : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreTurbulentConversion() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Common, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(IsUpgraded ? 10 : 7)
            .FromCard(this)
            .Targeting(cardPlay.Target!)
            .Execute(choiceContext);

        var silver = SecondaryResourceCmd.Get(Owner, SilverKeyResource.ResourceId);
        if (silver <= 0) return;

        var per = IsUpgraded ? 3 : 2;
        await DamageCmd.Attack(per)
            .WithHitCount(silver)
            .FromCard(this)
            .TargetingRandomOpponents(CombatState!, allowDuplicates: true)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreSilverCoreExtraction : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreSilverCoreExtraction() : base(baseCost: 0, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var current = SecondaryResourceCmd.Get(Owner, SilverKeyResource.ResourceId);
        var max = SecondaryResourceCmd.GetMax(Owner, SilverKeyResource.ResourceId) ?? 5;
        var gain = IsUpgraded ? 2 : 1;
        var overflow = Math.Max(0, current + gain - max);

        await SecondaryResourceCmd.Gain(Owner, SilverKeyResource.ResourceId, gain, this);

        if (overflow > 0)
            await CardPileCmd.Draw(choiceContext, overflow, Owner);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreDisplacementAlpha : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreDisplacementAlpha() : base(baseCost: 2, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var amount = SecondaryResourceCmd.Get(Owner, SilverKeyResource.ResourceId);
        if (amount <= 0) return;

        await SecondaryResourceCmd.Spend(Owner, SilverKeyResource.ResourceId, amount, this, this);
        await PlayerCmd.GainEnergy(amount, Owner);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreCoreFocus : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreCoreFocus() : base(baseCost: 0, type: CardType.Skill, rarity: CardRarity.Common, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await SecondaryResourceCmd.Gain(Owner, SilverKeyResource.ResourceId, IsUpgraded ? 3 : 2, this);
        await CardPileCmd.Draw(choiceContext, 1, Owner);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreSteadyExtraction : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(11, ValueProp.Move)
    ];

    public DoreSteadyExtraction() : base(baseCost: 2, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, false);

        var silver = SecondaryResourceCmd.Get(Owner, SilverKeyResource.ResourceId);
        if (silver >= 5)
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, false);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
    }
}
