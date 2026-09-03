using System.Linq;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using Foreve.Scripts.Content.Powers;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanUntamedBlade : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    public RotanUntamedBlade() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.AnyPlayer, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 回响机制（本回合打击造成伤害后对随机敌人追加等量伤害）——本次不动，留给下一批
        await PowerCmd.Apply<RotanUntamedBladePower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);

        // 从抽牌堆获得一张打击：基础=随机，升级=手动选择（参考铁纪的选择写法）
        // 牌堆（抽牌堆）没有打击时直接跳过
        var strikes = Owner.PlayerCombatState.DrawPile.Cards
            .Where(c => RotanTags.IsStrikeCard(c))
            .ToList();
        if (strikes.Count == 0) return;

        if (!IsUpgraded)
        {
            var picked = strikes[new Random().Next(strikes.Count)];
            await CardPileCmd.Add(picked, PileType.Hand);
            return;
        }

        var pilePrefs = new CardSelectorPrefs(
            new LocString("foreve_I18N_cards", "FOREVE_CARD_SELECT_UNTAMED_BLADE_PILE"),
            1, 1)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };

        var pileResult = await CardSelectCmd.FromCombatPile(
            choiceContext, Owner.PlayerCombatState.DrawPile, Owner, pilePrefs,
            c => RotanTags.IsStrikeCard(c));
        var pickedCard = pileResult.FirstOrDefault();
        if (pickedCard == null) return;

        await CardPileCmd.Add(pickedCard, PileType.Hand);
    }

    protected override void OnUpgrade()
    {
        // 升级改为手动选择（逻辑在 OnPlay 的 IsUpgraded 分支）
    }
}
