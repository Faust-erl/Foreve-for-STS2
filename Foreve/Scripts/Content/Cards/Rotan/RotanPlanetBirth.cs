using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanPlanetBirth : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(1, ValueProp.Move)
    ];

    public RotanPlanetBirth() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.AnyPlayer, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CardPileCmd.Draw(choiceContext, IsUpgraded ? 2 : 1, Owner);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, false);
        await PowerCmd.Apply<DexterityPower>(choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // 升级（星球初生+）：抽 2 张牌、获得 2 点格挡、获得 2 点敏捷（设计文档 2/2/2）
        DynamicVars.Block.UpgradeValueBy(1);
    }
}
