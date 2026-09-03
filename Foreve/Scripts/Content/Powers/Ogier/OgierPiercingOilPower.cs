using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Combat;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierPiercingOilPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    protected override bool IsVisibleInternal => false;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_piercing_oil.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_piercing_oil_big.png"
    );

    public override Decimal ModifyDamageAdditive(
        Creature target,
        Decimal amount,
        ValueProp props,
        Creature dealer,
        CardModel? cardSource)
    {
        // 与力量/荣誉同款判定（IsPoweredAttack）：穿刺伤害实际结算带 Move|Unblockable|SkipHurtAnim，
        // 精确比较 props == ValueProp.Move 会让牌面预览翻倍、实际穿刺伤害不翻倍。
        if (dealer == Owner && props.IsPoweredAttack())
            return amount; // 伤害翻倍：amount + amount = amount * 2
        return 0;
    }

    public override async Task AfterDamageGiven(
        PlayerChoiceContext ctx,
        Creature dealer,
        DamageResult result,
        ValueProp props,
        Creature target,
        CardModel? cardSource)
    {
        if (dealer == Owner && props.IsPoweredAttack())
        {
            // 对同目标追加穿刺伤害（翻倍效果）
            // 额外造成等量穿刺伤害
            if (cardSource != null)
            {
                var player = Owner.Player;
                if (player != null)
                    await OgierPiercingDamage.Deal(ctx, result.TotalDamage, target, cardSource, player);
            }
            await PowerCmd.Remove(this);
        }
    }
}
