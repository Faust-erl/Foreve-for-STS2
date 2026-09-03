using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Content.Powers.Ogier;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierSacrifice : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [];

    public OgierSacrifice() : base(baseCost: 0, type: CardType.Power, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<OgierSacrificePower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // 升级后获得固有（Innate）——游戏 Keywords 缓存不随 IsUpgraded 刷新，须在 OnUpgrade 动态添加
        AddKeyword(CardKeyword.Innate);
    }
}
