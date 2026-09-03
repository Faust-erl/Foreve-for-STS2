using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace Foreve.Scripts.Combat;

public static class OgierPiercingDamage
{
    /// <summary>
    /// 穿刺伤害：HP 直伤不可格挡，同时拆等量格挡（不超过当前值）。
    /// 例：8 格挡 / 12 生命 + 10 点穿刺 → 格挡归零、生命减至 2。
    ///
    /// props = Move | Unblockable | SkipHurtAnim：
    ///   - Unblockable：Creature.DamageBlockInternal 对带该标记的伤害格挡吸收为 0
    ///     （IL：HasFlag(Unblockable) 时 blocked=0）→ HP 吃满额伤害，不受格挡抵消；
    ///     同时格挡不会在这次伤害里被扣，改由下方 LoseBlock 单独拆等量格挡（不产生溢出生命伤害）。
    ///   - Move：IsPoweredAttack()=true → 力量/易伤修正生效（设计文档：穿刺伤害同样享受力量加成）。
    ///   - SkipHurtAnim：不播受击动画/受击音效（沿用旧视觉，伤害数字/火花仍正常显示）。
    ///
    /// 注意：ValueProp.Unpowered 是「荆棘式可格挡伤害」标记（DamageProps.nonCardUnpowered，
    /// ThornsPower 反击同款），不是绕格挡标记；绕格挡的标记是 Unblockable
    /// （旧注释「Unblockable 是死标记」是误判：IL 调用点以字面量 ldc.i4.2 出现，
    /// 搜枚举名搜不到引用）。
    /// </summary>
    public static async Task Deal(
        PlayerChoiceContext ctx,
        Decimal amount,
        Creature target,
        CardModel cardSource,
        Player player)
    {
        // HP 直伤（绕格挡，受力量/易伤修正）
        var results = await CreatureCmd.Damage(
            ctx, target, amount, ValueProp.Move | ValueProp.Unblockable | ValueProp.SkipHurtAnim, player.Creature, cardSource);

        // 按实际造成伤害（含力量/易伤加成）拆等量格挡，不超过当前值
        var dealt = (Decimal)(results.FirstOrDefault()?.TotalDamage ?? (int)amount);
        var blockLoss = Decimal.Min(dealt, target.Block);
        if (blockLoss > 0)
            await CreatureCmd.LoseBlock(target, blockLoss);
    }
}
