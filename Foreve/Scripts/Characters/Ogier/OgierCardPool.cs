using System.Linq;
using Godot;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Utils;

namespace Foreve.Scripts.Characters.Ogier;

public class OgierCardPool : TypeListCardPoolModel
{
    public override string Title => "ogier";
    public override string EnergyColorName => "ogier";

    public override string? TextEnergyIconPath => "res://Foreve/Assets/UI/energy_shared_text.png";
    public override string? BigEnergyIconPath => "res://Foreve/Assets/UI/energy_shared_big.png";

    public override Color DeckEntryCardColor => new(0.85f, 0.7f, 0.2f);
    public override Color EnergyOutlineColor => new(0.85f, 0.7f, 0.2f);

    private static readonly Material? _poolFrameMaterial =
        MaterialUtils.CreateReplaceHueShaderMaterial(0.85f, 0.7f, 0.2f);

    public override Material? PoolFrameMaterial => _poolFrameMaterial;

    public override bool IsColorless => false;
}
