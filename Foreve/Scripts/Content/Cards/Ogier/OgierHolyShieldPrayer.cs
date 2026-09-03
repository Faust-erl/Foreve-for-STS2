using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Characters.Ogier;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierHolyShieldPrayer : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [];

    public OgierHolyShieldPrayer() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var honor = SecondaryResourceCmd.Get(Owner, OgierCharacter.HonorResourceId);
        var blockAmount = honor * 5;

        await CreatureCmd.GainBlock(Owner.Creature, blockAmount, ValueProp.Move, cardPlay, false);
    }

    // 基础版条件保留：回合结束时若荣誉 ≥ 3 且此牌在手牌中，本回合保留
    // （升级版在 OnUpgrade 里动态加 Retain，勿动；与盾击共用 OgierConditionalRetain 辅助）
    public override Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> participants)
    {
        OgierConditionalRetain.TryGiveRetainIfHonor(this, ctx, side, 3);
        return Task.CompletedTask;
    }

    protected override void OnUpgrade()
    {
        // 升级后获得保留（Retain）——游戏 Keywords 缓存不随 IsUpgraded 刷新，须在 OnUpgrade 动态添加
        AddKeyword(CardKeyword.Retain);
    }
}
