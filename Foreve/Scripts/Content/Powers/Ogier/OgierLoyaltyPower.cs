using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierLoyaltyPower : ModPowerTemplate
{
    private bool _damaged;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_loyalty.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_loyalty_big.png"
    );

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> sideCreatures)
    {
        if (side != CombatSide.Player) return;
        if (_damaged) return;
        // Owner is always Player, skip check

        var player = Owner.Player;
        if (player == null) return;
        var blockAmount = Amount > 1 ? 5m : 3m;
        await SecondaryResourceCmd.Gain(player, Characters.Ogier.OgierCharacter.HonorResourceId, 1, this);
        await CreatureCmd.GainBlock(Owner, blockAmount, ValueProp.Move, null, false);
    }

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature dealer,
        CardModel? cardSource)
    {
        if (target == Owner && result.TotalDamage > 0)
        {
            _damaged = true;
        }
    }
}
