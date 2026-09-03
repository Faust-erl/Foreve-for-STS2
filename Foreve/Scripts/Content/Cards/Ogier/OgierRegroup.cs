using System.Linq;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierRegroup : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(4, ValueProp.Move)
    ];

    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public OgierRegroup() : base(baseCost: 0, type: CardType.Skill, rarity: CardRarity.Common, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block.BaseValue, ValueProp.Move, cardPlay, false);

        // 从弃牌堆筛选技能牌
        var skillCards = Owner.PlayerCombatState.DiscardPile.Cards
            .Where(c => c.Type == CardType.Skill)
            .ToList();
        if (skillCards.Count == 0) return;

        if (!IsUpgraded)
        {
            // 未升级（重整旗鼓）：随机将弃牌堆中的 1 张技能牌加入手牌
            var skillCard = skillCards[new Random().Next(skillCards.Count)];
            await CardPileCmd.Add(skillCard, PileType.Hand);
            return;
        }

        // 升级后（重整旗鼓+）：玩家从弃牌堆手动选择 1 张技能牌加入手牌
        var pilePrefs = new CardSelectorPrefs(
            new LocString("foreve_I18N_cards", "FOREVE_CARD_SELECT_REGROUP_PILE"),
            1, 1)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };

        var pileResult = await CardSelectCmd.FromCombatPile(
            choiceContext, Owner.PlayerCombatState.DiscardPile, Owner, pilePrefs,
            c => c.Type == CardType.Skill);
        var picked = pileResult.FirstOrDefault();
        if (picked == null) return;

        await CardPileCmd.Add(picked, PileType.Hand);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2);
    }
}
