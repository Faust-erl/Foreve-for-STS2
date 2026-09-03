using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using Foreve.Scripts.Content.Cards.Ogier;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierBrokenOathPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_broken_oath.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_broken_oath_big.png"
    );

    // 此牌在手牌中的玩家回合结束时，失去 3 点格挡（到 0 为止）
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> sideCreatures)
    {
        if (side != CombatSide.Player) return;
        // 「此牌在手牌中」判定：持有者玩家的手牌里存在破碎的誓言（CardModel.get_Pile() == Hand 等价判定）
        var player = CombatState.Players.FirstOrDefault(p => p.Creature == Owner);
        if (player == null) return;
        if (!player.PlayerCombatState.Hand.Cards.Any(c => c is OgierBrokenOath)) return;
        if (Owner.Block <= 0) return;
        var loss = Decimal.Min(3, Owner.Block);
        await CreatureCmd.LoseBlock(Owner, loss);
    }
}
