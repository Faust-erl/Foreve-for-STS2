using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Characters.Ogier;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierHoldGroundEndTurnPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_hold_ground_end_turn.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_hold_ground_end_turn_big.png"
    );

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> sideCreatures)
    {
        if (side != CombatSide.Enemy) return;
        if (Owner.Block <= 0) return;
        var player = Owner.Player;
        if (player == null) return;

        await SecondaryResourceCmd.Gain(player, OgierCharacter.HonorResourceId, 1, this);
        await PowerCmd.Remove(this);
    }
}
