using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 商店角色 PlayAnimation 兼容补丁。
/// 原版 NMerchantCharacter.PlayAnimation 把子节点当 Spine 处理（new MegaSprite + GetAnimationState，
/// 对 AnimatedSprite2D 会得到 null 再 NRE）。游戏 GameOver 画面会对商店角色调
/// PlayAnimation("die", false)（NGameOverScreen.MoveCreaturesToDifferentLayerAndDisableUi，IL 实证）。
/// Prefix：检测到子节点是 AnimatedSprite2D 时由 mod 自己播放并跳过原方法；
/// 原版 Spine 场景不受影响（走原逻辑）。
/// </summary>
[HarmonyPatch(typeof(NMerchantCharacter), "PlayAnimation")]
public static class MerchantCharacterPlayAnimationPatch
{
    public static bool Prefix(NMerchantCharacter __instance, string anim, bool loop)
    {
        if (__instance.GetChildCount() <= 0)
        {
            return true;
        }

        var child = __instance.GetChild(0, false) as AnimatedSprite2D;
        if (child == null || child.SpriteFrames == null)
        {
            return true; // 原版 Spine 场景，走原逻辑
        }

        // mod 帧动画场景：有该动画就播，没有则回待机
        child.Play(child.SpriteFrames.HasAnimation(anim) ? anim : "idle");
        return false;
    }
}
