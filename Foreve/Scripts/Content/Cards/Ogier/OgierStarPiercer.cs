using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using STS2RitsuLib.Cards.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Combat;
using Foreve.Scripts.Content.Powers.Ogier;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
[RegisterDustyTomeCard(typeof(Characters.Ogier.OgierCharacter))]
public class OgierStarPiercer : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png",
        VisualStyle: CardVisualStyle.Ancient
    );

    // CanonicalVars removed - values hardcoded

    public OgierStarPiercer() : base(baseCost: 2, type: CardType.Attack, rarity: CardRarity.Ancient, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target!;
        var vulnAmount = IsUpgraded ? 3 : (int)2;
        await PowerCmd.Apply<VulnerablePower>(choiceContext, target, vulnAmount, Owner.Creature, null, false);

        var pierceDmg = IsUpgraded ? 12 : (int)10;
        await OgierPiercingDamage.Deal(choiceContext, pierceDmg, target, this, Owner);

        var bleedAmount = IsUpgraded ? 12 : (int)10;
        await PowerCmd.Apply<OgierBleedPower>(choiceContext, target, bleedAmount, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // vuln 2→3, dmg 10→12, bleed 10→12 handled in OnPlay
    }
}
