using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierCompassionPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_compassion.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_compassion_big.png"
    );

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player.Creature != Owner) return;
        var hasDebuff = Owner.Powers.Any(p => p.Type == PowerType.Debuff);
        if (hasDebuff)
        {
            var blockAmount = Amount > 1 ? 7m : 4m;
            await CreatureCmd.GainBlock(Owner, blockAmount, ValueProp.Move, null, false);
        }
    }
}
