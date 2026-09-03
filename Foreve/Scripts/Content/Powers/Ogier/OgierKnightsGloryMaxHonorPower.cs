using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierKnightsGloryMaxHonorPower : ModPowerTemplate, ISecondaryResourceHookListener
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_knights_glory_max_honor.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_knights_glory_max_honor_big.png"
    );

    public Decimal ModifyMaxSecondaryResource(SecondaryResourceMaxContext ctx, Decimal currentMax)
    {
        if (ctx.Player.Creature == Owner && ctx.Definition.Id == Characters.Ogier.OgierCharacter.HonorResourceId)
            return currentMax + 4;
        return currentMax;
    }
}
