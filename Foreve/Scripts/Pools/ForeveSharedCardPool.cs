using Godot;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Utils;

namespace Foreve.Scripts.Pools;

public class ForeveSharedCardPool : TypeListCardPoolModel
{
    public override string Title => "foreve_shared";
    public override string EnergyColorName => "foreve_shared";

    public override string? TextEnergyIconPath => "res://Foreve/Assets/UI/energy_shared_text.png";
    public override string? BigEnergyIconPath => "res://Foreve/Assets/UI/energy_shared_big.png";

    public override Color DeckEntryCardColor => new(0.8f, 0.5f, 0.9f);
    public override Color EnergyOutlineColor => new(0.8f, 0.5f, 0.9f);

    private static readonly Material? _poolFrameMaterial =
        MaterialUtils.CreateReplaceHueShaderMaterial(0.8f, 0.5f, 0.9f);

    public override Material? PoolFrameMaterial => _poolFrameMaterial;

    public override bool IsColorless => true;
}
