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

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierPenetratingStrike : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    // CanonicalVars removed - values hardcoded

    public OgierPenetratingStrike() : base(baseCost: 2, type: CardType.Attack, rarity: CardRarity.Uncommon, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target!;
        // 升级（穿透打击+）：穿刺伤害 12→15
        var pierceDmg = IsUpgraded ? 15 : 12;
        await OgierPiercingDamage.Deal(choiceContext, pierceDmg, target, this, Owner);

        // 下回合开始时施加易伤 - 用 delayed power
        var vulnAmount = IsUpgraded ? 2 : (int)1;
        await PowerCmd.Apply<VulnerablePower>(choiceContext, target, vulnAmount, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // UpgradeValueBy removed(3);
        // vulnerable 1→2 handled in OnPlay
    }
}
