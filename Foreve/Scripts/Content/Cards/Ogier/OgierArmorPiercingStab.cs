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
public class OgierArmorPiercingStab : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    // CanonicalVars removed - values hardcoded

    public OgierArmorPiercingStab() : base(baseCost: 2, type: CardType.Attack, rarity: CardRarity.Rare, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target!;
        // 升级（穿甲刺击+）：穿刺伤害 9→12
        var pierceDmg = IsUpgraded ? 12 : 9;
        await OgierPiercingDamage.Deal(choiceContext, pierceDmg, target, this, Owner);

        await PowerCmd.Apply<VulnerablePower>(choiceContext, target, (int)2, Owner.Creature, null, false);

        var weakAmount = IsUpgraded ? 2 : (int)1;
        await PowerCmd.Apply<WeakPower>(choiceContext, target, weakAmount, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // UpgradeValueBy removed(3);
        // weak 1→2 handled in OnPlay
    }
}
