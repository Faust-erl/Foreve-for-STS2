using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanBehemothRampage : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    // X 费卡（与不定壁垒同款声明）：费用 = 投入能量，打出张数按 EnergySpent 计算
    protected override bool HasEnergyCostX => true;

    // 描述里的「获得1e」能量图标标记：EnergyVar 默认名 "Energy"，配合 {Energy:energyIcons()} 渲染
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];

    public RotanBehemothRampage() : base(baseCost: -1, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.AnyPlayer, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // X 费语义接通：打出张数 = 投入能量数（EnergySpent），升级后 = EnergySpent + 1
        // （1费基础1张/升级2张，2费基础2张/升级3张——与描述「打出X张/X+1张」一致）
        var drawCount = cardPlay.Resources.EnergySpent;
        if (IsUpgraded) drawCount++;
        await CardPileCmd.AutoPlayFromDrawPile(choiceContext, Owner, drawCount, CardPilePosition.Top, false);

        // 获得 1 能量
        await PlayerCmd.GainEnergy(1, Owner);
    }
}
