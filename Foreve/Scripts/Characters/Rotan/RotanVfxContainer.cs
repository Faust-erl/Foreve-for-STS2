using Godot;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace Foreve.Scripts.Characters.Rotan;

/// <summary>
/// 萝坦能量计数器粒子容器（NParticlesContainer 子类），挂在 %EnergyVfxBack/%EnergyVfxFront 节点上。
/// 空实现：当前无自定义粒子特效，占位保证 NEnergyCounter._Ready 的 GetNode 类型匹配。
/// </summary>
public partial class RotanVfxContainer : NParticlesContainer
{
}
