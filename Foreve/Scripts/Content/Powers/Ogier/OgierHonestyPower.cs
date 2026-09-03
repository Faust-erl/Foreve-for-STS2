using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierHonestyPower : ModPowerTemplate, ISecondaryResourceHookListener
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_honesty.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_honesty_big.png"
    );

    public Decimal ModifySecondaryResourceGain(SecondaryResourceContext ctx, Decimal amount)
    {
        if (ctx.Player.Creature == Owner && ctx.Definition.Id == Characters.Ogier.OgierCharacter.HonorResourceId && amount > 0)
            return amount + 1;
        return amount;
    }
}
