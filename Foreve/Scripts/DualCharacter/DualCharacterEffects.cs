using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Foreve.Scripts.DualCharacter;

/// <summary>
/// 双角色模式：mod 遗物全队生效辅助（2026-08-15 用户新规格，linker 分支）。
///
/// 玩法规格：主、副角色的专属遗物（含初始遗物）对两名角色以及所有卡牌都适用；
/// 但角色专属系统（例如奥吉尔的荣誉标记）仍只属于奥吉尔本人。
/// 因此 mod 遗物触发的「对己」增益（格挡/力量/护甲等）在双人模式下给全部存活
/// 玩家 creature；单玩家模式行为零变化（只作用于遗物 Owner）。
///
/// 用法（mod 遗物效果触发处）：
///   foreach (var creature in DualCharacterEffects.GetAlivePlayerCreatures(player, combat))
///       await PowerCmd.Apply&lt;XxxPower&gt;(choiceContext, creature, amount, creature, null, false);
///
/// ⚠️ 只用于 mod 遗物（Scripts/Content/Relics/ 下）；原版遗物不改造。
/// 本类无接线点：由各 mod 遗物直接调用。
/// </summary>
public static class DualCharacterEffects
{
    /// <summary>
    /// 取应获得「对己增益」的玩家 creature 列表：
    /// 双人模式 = 全部存活玩家 creature（主+副）；单玩家模式 = Owner creature（零变化）。
    /// combatState 为 null（非战斗上下文）时退化为只返回 Owner creature。
    /// </summary>
    public static IEnumerable<Creature> GetAlivePlayerCreatures(Player? owner, ICombatState? combatState)
    {
        if (!DualCharacterState.IsDualMode(combatState))
        {
            var own = owner?.Creature;
            if (own != null && !own.IsDead)
                yield return own;
            yield break;
        }

        foreach (var player in combatState!.Players)
        {
            var creature = player?.Creature;
            if (creature != null && !creature.IsDead)
                yield return creature;
        }
    }

    /// <summary>
    /// 判断 target 是否为当前战斗中的某名玩家 creature（主/副皆可）。
    /// 用于战斗级 hook（例如攻击命中）把「任意角色完美格挡」与敌人目标区分开。
    /// </summary>
    public static bool IsPlayerCreature(ICombatState? combatState, Creature? target)
    {
        if (combatState == null || target == null) return false;
        foreach (var player in combatState.Players)
        {
            if (ReferenceEquals(player?.Creature, target)) return true;
        }
        return false;
    }
}
