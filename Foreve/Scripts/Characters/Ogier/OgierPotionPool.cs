using System.Linq;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Characters.Ogier;

public class OgierPotionPool : TypeListPotionPoolModel
{
    public override string? TextEnergyIconPath => "res://Foreve/Assets/UI/energy_shared_text.png";
    public override string? BigEnergyIconPath => "res://Foreve/Assets/UI/energy_shared_big.png";
    public override string EnergyColorName => "ogier";
}
