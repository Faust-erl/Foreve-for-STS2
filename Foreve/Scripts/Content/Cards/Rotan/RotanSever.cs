using System.Linq;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.CardTags;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanSever : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(13, ValueProp.Move)
    ];

    // 升级后才「视为打击」：基础版无 Strike 标签，升级版在 OnUpgrade 动态添加（游戏 Tags 缓存不随 IsUpgraded 刷新）
    protected override HashSet<CardTag> CanonicalTags => [];

    public RotanSever() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Common, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 消耗 1 张手牌（打出时本牌已离手进 Play 堆，手牌只剩本牌/为空时跳过消耗，只打伤害）
        var hand = Owner.PlayerCombatState.Hand.Cards.ToList();
        if (hand.Count > 1)
        {
            var handPrefs = new CardSelectorPrefs(
                new LocString("foreve_I18N_cards", "FOREVE_CARD_SELECT_SEVER_HAND"),
                Math.Min(1, hand.Count), 1) // min 按实际手牌数钳制防软锁
            {
                Cancelable = false,
                RequireManualConfirmation = true
            };

            var handResult = await CardSelectCmd.FromHand(choiceContext, Owner, handPrefs, null, this);
            var picked = handResult.FirstOrDefault();
            if (picked != null)
                await CardPileCmd.Add(picked, PileType.Exhaust);
        }

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this)
            .Targeting(cardPlay.Target!)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        // 升级后视为打击：动态添加 Strike + rotanstrike 标签（Tags 缓存不随 IsUpgraded 刷新）
        this.AddModCardTag(CardTag.Strike);
        this.AddModCardTag(RotanTags.RotanStrikeTag);
    }
}
