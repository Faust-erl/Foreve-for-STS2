using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Content.Powers.Ogier;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierLoyalty : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    public OgierLoyalty() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 升级（忠诚+）：回合结束获得格挡 3→5（Power 内按 Amount>1 分支）
        await PowerCmd.Apply<OgierLoyaltyPower>(choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // 升级效果（格挡 3→5）由 OnPlay 传 Amount=2 触发 Power 内分支
    }
}
