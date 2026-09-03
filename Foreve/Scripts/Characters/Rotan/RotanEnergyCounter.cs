using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Foreve.Scripts.Characters.Rotan;

/// <summary>
/// 萝坦能量计数器根节点（NEnergyCounter 子类）。
/// 场景：res://scenes/combat/energy_counters/foreve_character_rotan_character_energy_counter.tscn
/// （约定路径，游戏按角色 id 直接加载；另有 Assets/Characters/Rotan/rotan_energy_counter.tscn 兜底）
/// 空实现：节点查找（%Label/%Layers/%RotationLayers/%EnergyVfxBack/%EnergyVfxFront）
/// 与能量球层切换逻辑全部来自游戏基类 NEnergyCounter。
/// </summary>
public partial class RotanEnergyCounter : NEnergyCounter
{
}
