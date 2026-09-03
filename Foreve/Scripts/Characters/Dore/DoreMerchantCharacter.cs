using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Foreve.Scripts.Characters.Dore;

public partial class DoreMerchantCharacter : NMerchantCharacter
{
    public override void _Ready()
    {
        var anim = GetChild(0, false) as AnimatedSprite2D;
        if (anim != null && anim.SpriteFrames != null && !anim.IsPlaying())
            anim.Play("idle");
    }
}
