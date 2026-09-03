using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace Foreve.Scripts.Characters;

/// <summary>
/// Foreve 角色战斗立绘公共基类。
/// 场景 %Visuals 是 AnimatedSprite2D（非 Spine），本类负责动作动画切换：
/// 播放一次性动画（attack/defense/skill1/skill2/awaken/burst/hurt）→ 播完自动回 "idle" 循环。
/// 由 PlayerCardAnimationController 订阅 RitsuLib CardPlayingEvent 后调用 PlayActionAnimation。
/// 动画源：Foreve/Assets/Characters/{角色}/anim/all_frames.tres（合并图集，含全部动作）。
/// </summary>
public abstract partial class ForeveCreatureVisualsBase : NCreatureVisuals
{
    /// <summary>待机动画名（与 SpriteFrames.tres 的动画名一致）。</summary>
    protected const string IdleAnimation = "idle";

    /// <summary>死亡动画名（与 SpriteFrames.tres 的动画名一致）。</summary>
    protected const string DeathAnimation = "death";

    /// <summary>动作动画缺失时的回退动画（萝坦无觉醒动画等场景）。</summary>
    protected const string FallbackActionAnimation = "defense";

    private AnimatedSprite2D? _anim;
    private bool _ready;
    /// <summary>待机/默认 scale（场景 AnimatedSprite2D scale=0.9，_Ready 时记录）。</summary>
    private Vector2 _baseScale = Vector2.One;
    /// <summary>是否处于死亡立绘状态：死亡动画播完停最后一帧，不再自动回待机。</summary>
    private bool _isDeadVisual;

    /// <summary>是否正在显示死亡立绘（用于复活时判断是否需要恢复待机）。</summary>
    public bool IsShowingDeath => _isDeadVisual;

    public override void _Ready()
    {
        base._Ready();
        // %Visuals 即 AnimatedSprite2D（场景契约；基类只做 Spine 分支，非 Spine 节点安全）
        _anim = GetNodeOrNull<AnimatedSprite2D>("%Visuals");
        if (_anim != null)
        {
            // 记录初始 scale（待机基准），回待机时恢复
            _baseScale = _anim.Scale;
            // 一次性动画播完回到待机循环
            _anim.AnimationFinished += OnAnimationFinished;
        }
        _ready = true;
        GD.Print($"[Foreve] {Name} 立绘动画就绪 (AnimatedSprite2D={(_anim != null ? "ok" : "missing")}, SpriteFrames={(_anim?.SpriteFrames != null ? "ok" : "missing")}, baseScale={_baseScale})");
    }

    private void OnAnimationFinished()
    {
        // 注意：AnimationFinished 在动画自然结束时发出，此刻 IsPlaying() 已为 false——
        // 不能加 IsPlaying() 检查，否则播完永远回不到 idle（2026-08-14 实测 bug）。
        // 循环动画 idle 不会触发该信号，此处安全。
        if (_anim == null) return;
        // 死亡动画播完保持最后一帧，直到复活（PlayIdle 显式恢复）。
        if (_isDeadVisual || _anim.Animation == DeathAnimation) return;
        if (_anim.Animation != IdleAnimation)
        {
            _anim.Play(IdleAnimation);
            // 回待机恢复默认 scale（动作动画期间保持基准 scale，见 PlayActionAnimation）
            _anim.Scale = _baseScale;
        }
    }

    /// <summary>
    /// 播放一次性动作动画（attack/defense/skill1/skill2/awaken/burst/hurt），播完自动回 idle。
    /// 同一动画重复触发会从头重播；动画不存在时回退 defense。
    /// （optimizer：移除成功路径日志，仅保留异常/回退诊断。）
    /// </summary>
    public void PlayActionAnimation(string animationName)
    {
        if (_anim == null || !_ready || !IsInsideTree())
        {
            GD.Print($"[Foreve] {Name} 播放动画跳过: 立绘未就绪 (anim={(_anim != null ? "ok" : "null")}, ready={_ready}, inTree={IsInsideTree()})");
            return;
        }
        // 死亡角色不再播普通动作动画（保持死亡最后一帧）。
        if (_isDeadVisual) return;
        if (_anim.SpriteFrames == null)
        {
            GD.Print($"[Foreve] {Name} 播放动画跳过: SpriteFrames 为空");
            return;
        }
        if (!_anim.SpriteFrames.HasAnimation(animationName))
        {
            // 目标角色没有该动画（如萝坦无 awaken）-> 回退防御动画
            GD.Print($"[Foreve] {Name} 动画 '{animationName}' 不存在，回退 '{FallbackActionAnimation}'");
            if (!_anim.SpriteFrames.HasAnimation(FallbackActionAnimation)) return;
            animationName = FallbackActionAnimation;
        }
        if (_anim.Animation == animationName && _anim.IsPlaying())
        {
            return; // 正在播放不打断
        }
        // 动作动画统一使用基准 scale（素材层已按待机本体高度统一尺寸，无需放大补偿）
        _anim.Scale = _baseScale;
        _anim.Play(animationName);
    }

    /// <summary>播放死亡动画并保持最后一帧（不自动回待机）。复活后调用 <see cref="PlayIdle"/> 恢复。</summary>
    public void PlayDeathAnimation()
    {
        if (_anim == null || !_ready || !IsInsideTree()) return;
        if (_anim.SpriteFrames == null) return;
        if (!_anim.SpriteFrames.HasAnimation(DeathAnimation))
        {
            GD.Print($"[Foreve] {Name} 死亡动画 '{DeathAnimation}' 不存在，保持当前帧");
            return;
        }
        _isDeadVisual = true;
        _anim.Scale = _baseScale;
        _anim.Play(DeathAnimation);
    }

    /// <summary>死亡/复活状态复位：回到待机循环。用于角色复活时恢复立绘。</summary>
    public void PlayIdle()
    {
        _isDeadVisual = false;
        if (_anim == null || !_ready) return;
        _anim.Scale = _baseScale;
        _anim.Play(IdleAnimation);
    }
}
