using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Foreve.Scripts.Characters.Rotan;

/// <summary>
/// 萝坦商店（Merchant）立绘节点。
/// 商店场景由 NMerchantRoom 以 Instantiate&lt;NMerchantCharacter&gt; 强转实例化，
/// 因此根节点必须是 NMerchantCharacter 子类。
/// 原版场景子节点是 SpineSprite（Spine 骨架），mod 用 AnimatedSprite2D 帧动画：
/// 这里 override _Ready（不调 base，基类会把 AnimatedSprite2D 当 Spine 处理），
/// 直接播放待机循环。GameOver 的 PlayAnimation("die") 由
/// MerchantCharacterPlayAnimationPatch（Harmony Prefix）兜底。
/// </summary>
public partial class RotanMerchantCharacter : NMerchantCharacter
{
    public override void _Ready()
    {
        // 不调 base._Ready()：基类 GetChild(0) → MegaSprite（Spine 专用），AnimatedSprite2D 会失败
        var anim = GetChild(0, false) as AnimatedSprite2D;
        if (anim != null && anim.SpriteFrames != null)
        {
            if (!anim.IsPlaying())
            {
                anim.Play("idle");
            }
        }
        GD.Print($"[Foreve] {Name} 商店立绘就绪 (AnimatedSprite2D={(anim != null ? "ok" : "missing")})");
    }
}
