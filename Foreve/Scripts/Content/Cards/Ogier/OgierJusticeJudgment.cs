using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using STS2RitsuLib.Cards.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Characters.Ogier;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierJusticeJudgment : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    // CanonicalVars removed - values hardcoded

    public OgierJusticeJudgment() : base(baseCost: 2, type: CardType.Attack, rarity: CardRarity.Uncommon, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target!;
        var honor = SecondaryResourceCmd.Get(Owner, OgierCharacter.HonorResourceId);
        var dmgPerHit = IsUpgraded ? 4 : (int)3;

        for (int i = 0; i < honor; i++)
        {
            await DamageCmd.Attack(dmgPerHit)
                .FromCard(this)
                .Targeting(target)
                .Execute(choiceContext);
        }

        await SecondaryResourceCmd.Spend(Owner, OgierCharacter.HonorResourceId, honor, this, this);

        var threshold = IsUpgraded ? 3 : 4;
        if (honor >= threshold)
        {
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
        }
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
        // dmg 3→4 and threshold 4→3 handled in OnPlay
    }
}
