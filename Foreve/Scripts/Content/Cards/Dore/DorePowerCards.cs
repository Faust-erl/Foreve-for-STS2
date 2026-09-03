using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Content.Powers.Dore;
using Foreve.Scripts.Content.Powers.Ogier;

namespace Foreve.Scripts.Content.Cards.Dore;

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
[RegisterCharacterStarterCard(typeof(Characters.Dore.DoreCharacter), 1)]
public class DoreOuterSurgery : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public DoreOuterSurgery() : base(baseCost: 2, type: CardType.Power, rarity: CardRarity.Basic, target: TargetType.AnyPlayer, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        foreach (var enemy in CombatState!.Enemies.Where(e => !e.IsDead).ToList())
        {
            await PowerCmd.Apply<WeakPower>(choiceContext, enemy, 2, Owner.Creature, null, false);
        }

        var target = cardPlay.Target ?? Owner.Creature;
        await PowerCmd.Apply<RegenPower>(choiceContext, target, 2, target, null, false);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreMindBodySplit : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreMindBodySplit() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<DoreMindBodySplitPower>(
            choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DorePureReason : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => BuildCanonicalKeywords();

    private static HashSet<CardKeyword> BuildCanonicalKeywords()
    {
        var set = new HashSet<CardKeyword>();
        try
        {
            if (Enum.TryParse<CardKeyword>("Ethereal", out var ethereal))
                set.Add(ethereal);
        }
        catch
        {
            // 无 Ethereal 枚举时本地化描述仍保留「虚无」。
        }
        return set;
    }

    public DorePureReason() : base(baseCost: 2, type: CardType.Power, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 既有负面状态保留但不再生效（AfterPowerAmountChanged 中即时移除新施加的负面状态）。
        var existing = Owner.Creature.Powers
            .Where(p => p is WeakPower or VulnerablePower or FrailPower)
            .ToList();
        foreach (var power in existing)
            await PowerCmd.Remove(power);

        await PowerCmd.Apply<DorePurityPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreForceFieldGenerator : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreForceFieldGenerator() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<DexterityPower>(choiceContext, Owner.Creature, 2, Owner.Creature, null, false);
        await PowerCmd.Apply<OgierGauntletArmorPower>(
            choiceContext, Owner.Creature, IsUpgraded ? 5 : 3, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreBeyondBodyPowerCard : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreBeyondBodyPowerCard() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<DoreBeyondBodyPower>(
            choiceContext, Owner.Creature, IsUpgraded ? 3 : 2, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreNoisyVoice : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreNoisyVoice() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<DexterityPower>(choiceContext, Owner.Creature, -1, Owner.Creature, null, false);
        await PowerCmd.Apply<DoreNoisyVoicePower>(
            choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreJointLubrication : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreJointLubrication() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<DoreJointLubricationPower>(
            choiceContext, Owner.Creature, IsUpgraded ? 4 : 3, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreSilverKeyReactor : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreSilverKeyReactor() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<DoreSilverKeyReactorPower>(
            choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreSilverKeyRadiation : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreSilverKeyRadiation() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<DoreSilverKeyRadiationPower>(
            choiceContext, Owner.Creature, IsUpgraded ? 3 : 2, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
    }
}
