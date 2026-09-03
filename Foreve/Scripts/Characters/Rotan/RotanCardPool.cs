using Godot;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Utils;

namespace Foreve.Scripts.Characters.Rotan;

public class RotanCardPool : TypeListCardPoolModel
{
    public override string Title => "rotan";
    public override string EnergyColorName => "rotan";

    public override string? TextEnergyIconPath => "res://Foreve/Assets/UI/energy_rotan_text.png";
    public override string? BigEnergyIconPath => "res://Foreve/Assets/Characters/Rotan/images/energy_rotan_big.png";

    public override Color DeckEntryCardColor => new(0.9f, 0.3f, 0.2f);
    public override Color EnergyOutlineColor => new(0.9f, 0.3f, 0.2f);

    private static readonly Material? _poolFrameMaterial =
        MaterialUtils.CreateReplaceHueShaderMaterial(0.9f, 0.3f, 0.2f);

    public override Material? PoolFrameMaterial => _poolFrameMaterial;

    public override bool IsColorless => false;
}
