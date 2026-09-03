using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Combat;
using Foreve.Scripts.Content.Powers.Ogier;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
[RegisterCharacterStarterCard(typeof(Characters.Ogier.OgierCharacter), 1)]
public class OgierPiercingSpear : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(8, ValueProp.Move)
    ];

    public OgierPiercingSpear() : base(baseCost: 2, type: CardType.Attack, rarity: CardRarity.Basic, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target!;

        // 穿刺伤害：无视格挡 HP 直伤 + 拆等量格挡（统一走 OgierPiercingDamage.Deal）
        await OgierPiercingDamage.Deal(choiceContext, DynamicVars.Damage.BaseValue, target, this, Owner);

        var bleedAmount = IsUpgraded ? 5 : 3;
        await PowerCmd.Apply<OgierBleedPower>(choiceContext, target, bleedAmount, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
    }
}
