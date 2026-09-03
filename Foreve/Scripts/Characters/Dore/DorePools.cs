using Godot;
using STS2RitsuLib.Scaffolding.Content;
using STS2RitsuLib.Utils;

namespace Foreve.Scripts.Characters.Dore;

public class DoreCardPool : TypeListCardPoolModel
{
    public override string Title => "dore";
    public override string EnergyColorName => "dore";

    public override string? TextEnergyIconPath =>
        ResourceLoader.Exists("res://Foreve/Assets/UI/energy_dore_text.png")
            ? "res://Foreve/Assets/UI/energy_dore_text.png"
            : "res://Foreve/Assets/UI/energy_shared_text.png";

    public override string? BigEnergyIconPath =>
        ResourceLoader.Exists("res://Foreve/Assets/Characters/Dore/images/energy_dore_big.png")
            ? "res://Foreve/Assets/Characters/Dore/images/energy_dore_big.png"
            : "res://Foreve/Assets/UI/energy_shared_big.png";

    public override Color DeckEntryCardColor => new(0.25f, 0.75f, 0.85f);
    public override Color EnergyOutlineColor => new(0.25f, 0.75f, 0.85f);

    private static readonly Material? _poolFrameMaterial =
        MaterialUtils.CreateReplaceHueShaderMaterial(0.25f, 0.75f, 0.85f);

    public override Material? PoolFrameMaterial => _poolFrameMaterial;

    public override bool IsColorless => false;
}

public class DoreRelicPool : TypeListRelicPoolModel
{
    public override string? TextEnergyIconPath =>
        ResourceLoader.Exists("res://Foreve/Assets/UI/energy_dore_text.png")
            ? "res://Foreve/Assets/UI/energy_dore_text.png"
            : "res://Foreve/Assets/UI/energy_shared_text.png";

    public override string? BigEnergyIconPath =>
        ResourceLoader.Exists("res://Foreve/Assets/Characters/Dore/images/energy_dore_big.png")
            ? "res://Foreve/Assets/Characters/Dore/images/energy_dore_big.png"
            : "res://Foreve/Assets/UI/energy_shared_big.png";
    public override string EnergyColorName => "dore";
}

public class DorePotionPool : TypeListPotionPoolModel
{
    public override string? TextEnergyIconPath =>
        ResourceLoader.Exists("res://Foreve/Assets/UI/energy_dore_text.png")
            ? "res://Foreve/Assets/UI/energy_dore_text.png"
            : "res://Foreve/Assets/UI/energy_shared_text.png";

    public override string? BigEnergyIconPath =>
        ResourceLoader.Exists("res://Foreve/Assets/Characters/Dore/images/energy_dore_big.png")
            ? "res://Foreve/Assets/Characters/Dore/images/energy_dore_big.png"
            : "res://Foreve/Assets/UI/energy_shared_big.png";
    public override string EnergyColorName => "dore";
}
