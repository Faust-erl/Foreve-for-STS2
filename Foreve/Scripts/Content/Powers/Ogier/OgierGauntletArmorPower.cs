using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierGauntletArmorPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_gauntlet_armor.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_gauntlet_armor_big.png"
    );

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> sideCreatures)
    {
        if (side != CombatSide.Player) return;
        if (Amount <= 0) return;
        await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Move, null, false);
    }

    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature dealer,
        CardModel? cardSource)
    {
        if (target != Owner) return;
        if (result.UnblockedDamage > 0 && Amount > 0)
        {
            await PowerCmd.ModifyAmount(choiceContext, this, -1, null, null, false);
        }
    }
}
