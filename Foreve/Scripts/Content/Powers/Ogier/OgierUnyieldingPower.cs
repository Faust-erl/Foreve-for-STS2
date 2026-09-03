using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierUnyieldingPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_unyielding.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_unyielding_big.png"
    );

    public override async Task BeforeDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        Decimal amount,
        ValueProp props,
        Creature dealer,
        CardModel? cardSource)
    {
        if (target != Owner) return;
        var player = Owner.Player;
        if (player == null) return;

        var honor = SecondaryResourceCmd.Get(player, Characters.Ogier.OgierCharacter.HonorResourceId);
        if (honor > 0)
        {
            var reduction = Decimal.Min(amount, honor);
            await CreatureCmd.GainBlock(Owner, reduction, ValueProp.Move, null, false);
        }
    }
}
