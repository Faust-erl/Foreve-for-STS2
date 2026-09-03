using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierBleedPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_bleed.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_bleed_big.png"
    );

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> sideCreatures, ICombatState combatState)
    {
        if (side != CombatSide.Enemy) return;
        if (!sideCreatures.Contains(Owner)) return;
        if (Amount <= 0) return;

        await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), Owner, Amount, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        await PowerCmd.Remove(this);
    }
}
