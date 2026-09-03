using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Content.Powers.Ogier;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierCourage : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    public OgierCourage() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 升级（勇气+）：受击后下回合力量 1→2（Power 内按 Amount>1 分支）
        await PowerCmd.Apply<OgierCouragePower>(choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // 升级效果（力量 1→2）由 OnPlay 传 Amount=2 触发 Power 内分支
    }
}
