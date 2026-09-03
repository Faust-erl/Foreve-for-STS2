using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Pools;

public class ForeveSharedRelicPool : TypeListRelicPoolModel
{
    public override string? TextEnergyIconPath => "res://Foreve/Assets/UI/energy_shared_text.png";
    public override string? BigEnergyIconPath => "res://Foreve/Assets/UI/energy_shared_big.png";
    public override string EnergyColorName => "foreve_shared";
}
