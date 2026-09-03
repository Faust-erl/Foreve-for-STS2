using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanBeyondFirmament : ModCardTemplate
{
    public RotanBeyondFirmament() : base(baseCost: 0, type: CardType.Power, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true)
    {
    }

    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    // 保留：回合结束时不弃牌（Flush 阶段按 ShouldRetainThisTurn 判定）
    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];

    // 本场战斗内累计保留次数：第 2 次回合结束时仍留在手牌 → 消耗
    private int _retainCount;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var drawCount = IsUpgraded ? 3 : 2;
        await CardPileCmd.Draw(choiceContext, drawCount, Owner);
        await PowerCmd.Apply<DexterityPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
    }

    // 弃牌发生在 BeforeSideTurnEnd 之后（FlushPlayerHand 阶段），hook 时保留牌仍在手牌，判定成立
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        if (Pile?.Type != PileType.Hand) return; // 战斗外 Pile 为 null，判空
        _retainCount++;
        if (_retainCount >= 2)
            await CardPileCmd.Add(this, PileType.Exhaust);
    }
}
