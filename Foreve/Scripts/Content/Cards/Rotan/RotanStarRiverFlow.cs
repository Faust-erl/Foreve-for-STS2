using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanStarRiverFlow : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(10, ValueProp.Move)
    ];

    public RotanStarRiverFlow() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.AnyPlayer, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (IsUpgraded)
        {
            // 升级（星河流转+）：抽 1 张非攻击牌——翻抽牌堆顶部，攻击牌弃置，直到抽到非攻击牌
            var drawPile = Owner.PlayerCombatState.DrawPile;
            var guard = 0;
            while (drawPile.Cards.Count > 0 && guard < 20)
            {
                guard++;
                var top = drawPile.Cards.First();
                if (top.Type == CardType.Attack)
                {
                    await CardPileCmd.Add(top, PileType.Discard);
                }
                else
                {
                    await CardPileCmd.Add(top, PileType.Hand);
                    break;
                }
            }
        }
        else
        {
            // 基础（星河流转）：丢弃手牌中的所有非攻击牌
            var hand = Owner.PlayerCombatState.Hand.Cards.ToList();
            foreach (var card in hand)
            {
                if (card.Type != CardType.Attack)
                    await CardPileCmd.Add(card, PileType.Discard);
            }
        }

        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, false);
    }

    protected override void OnUpgrade()
    {
        // 升级效果（抽 1 张非攻击牌）在 OnPlay 中按 IsUpgraded 处理
    }
}
