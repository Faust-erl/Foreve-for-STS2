using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using Foreve.Scripts.Characters.Ogier;
using Foreve.Scripts.DualCharacter;
using STS2RitsuLib.Combat.SecondaryResources;

namespace Foreve.Scripts.Content.Cards.Ogier;

/// <summary>
/// 圣盾祷言/盾击共用的「回合结束按荣誉条件保留」辅助。
/// 玩家回合结束时，若荣誉 ≥ 阈值且牌仍在手牌，调用 GiveSingleTurnRetain 给予单回合保留标记
/// （弃牌发生在 BeforeSideTurnEnd 之后的 Flush 阶段，标记在弃牌后由 EndOfTurnCleanup 自动清除，无副作用）。
/// </summary>
public static class OgierConditionalRetain
{
    public static void TryGiveRetainIfHonor(CardModel card, PlayerChoiceContext ctx, CombatSide side, int minHonor)
    {
        if (side != CombatSide.Player) return;
        if (card.Pile?.Type != PileType.Hand) return; // 战斗外 Pile 为 null，判空
        // 回合结束条件保留时卡牌 Owner 已恢复为主玩家（双人合并牌库），荣誉必须读奥吉尔玩家。
        var honor = SecondaryResourceCmd.Get(
            DualCharacterRelicScoping.ResolveOgierPlayer(card.Owner),
            OgierCharacter.HonorResourceId);
        if (honor >= minHonor)
            card.GiveSingleTurnRetain();
    }
}
