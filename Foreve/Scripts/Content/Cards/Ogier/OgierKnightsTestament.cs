using System;
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
public class OgierKnightsTestament : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public OgierKnightsTestament() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var combatState = Owner.PlayerCombatState;
        var hand = combatState.Hand.Cards.ToList();
        if (hand.Count == 0) return;

        // 选择手牌中的 2 张牌（手牌不足 2 张时最少可选 1 张，避免选择界面无法确认）
        var prefs = new CardSelectorPrefs(
            new LocString("foreve_I18N_cards", "FOREVE_CARD_SELECT_KNIGHTS_TESTAMENT_HAND"),
            Math.Min(2, hand.Count), 2)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };

        var selected = await CardSelectCmd.FromHand(choiceContext, Owner, prefs, null, this);
        foreach (var card in selected)
        {
            // 附加 Retain 关键字 = 本场战斗中保留（卡牌实例生命周期即本场战斗，战斗结束实例销毁）
            CardCmd.ApplyKeyword(card, [CardKeyword.Retain]);
        }
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}
