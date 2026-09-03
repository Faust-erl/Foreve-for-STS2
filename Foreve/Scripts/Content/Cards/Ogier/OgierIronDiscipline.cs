using System.Linq;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierIronDiscipline : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public OgierIronDiscipline() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Common, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var combatState = Owner.PlayerCombatState;

        // 先查后换：抽牌堆（升级版再查弃牌堆）无技能牌 → 整个效果不执行（不弹选择、不移动任何牌）
        var hasSkillInDrawPile = combatState.DrawPile.Cards.Any(c => c.Type == CardType.Skill);
        var hasSkillInDiscardPile = IsUpgraded && combatState.DiscardPile.Cards.Any(c => c.Type == CardType.Skill);
        if (!hasSkillInDrawPile && !hasSkillInDiscardPile) return;

        var hand = combatState.Hand.Cards.ToList();
        if (hand.Count == 0) return;

        // 1) 从手牌中选择 1 张
        var handPrefs = new CardSelectorPrefs(
            new LocString("foreve_I18N_cards", "FOREVE_CARD_SELECT_IRON_DISCIPLINE_HAND"),
            1, 1)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };

        var handResult = await CardSelectCmd.FromHand(choiceContext, Owner, handPrefs, null, this);
        var handCard = handResult.FirstOrDefault();
        if (handCard == null) return;

        // 将选中的牌移入抽牌堆
        await CardPileCmd.Add(handCard, PileType.Draw);

        // 2) 从抽牌堆（升级后可检索弃牌堆）选择 1 张技能牌
        var drawCards = combatState.DrawPile.Cards.ToList();
        var hasSkillInDraw = drawCards.Any(c => c.Type == CardType.Skill);

        CardPile? sourcePile;
        if (hasSkillInDraw)
            sourcePile = combatState.DrawPile;
        else if (IsUpgraded)
            sourcePile = combatState.DiscardPile;
        else
            return;

        var pilePrefs = new CardSelectorPrefs(
            new LocString("foreve_I18N_cards", "FOREVE_CARD_SELECT_IRON_DISCIPLINE_PILE"),
            1, 1)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };

        var pileResult = await CardSelectCmd.FromCombatPile(
            choiceContext, sourcePile, Owner, pilePrefs,
            c => c.Type == CardType.Skill);
        var skillCard = pileResult.FirstOrDefault();
        if (skillCard == null) return;

        // 将选中的技能牌移入手牌
        await CardPileCmd.Add(skillCard, PileType.Hand);
    }

    protected override void OnUpgrade()
    {
        // 升级后检索范围从牌堆扩展到弃牌堆（逻辑在 OnPlay 中）
    }
}
