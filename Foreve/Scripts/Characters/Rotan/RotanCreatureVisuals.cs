using Foreve.Scripts.Characters;

namespace Foreve.Scripts.Characters.Rotan;

/// <summary>
/// 萝坦战斗立绘根节点（ForeveCreatureVisualsBase 子类，基类为游戏 NCreatureVisuals）。
/// 场景：res://Foreve/Assets/Characters/Rotan/rotan_character.tscn
/// （以及约定路径 res://scenes/creature_visuals/foreve_character_rotan_character.tscn 兜底）
/// %Visuals 使用 AnimatedSprite2D：待机 idle（图集版 idle_frames.tres，循环），
/// 打牌时由 PlayerCardAnimationController 触发 attack/defense 一次性动画，播完自动回 idle。
/// </summary>
public partial class RotanCreatureVisuals : ForeveCreatureVisualsBase
{
}
