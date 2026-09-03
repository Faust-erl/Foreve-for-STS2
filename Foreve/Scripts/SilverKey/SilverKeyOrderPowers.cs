using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.SilverKey;

/// <summary>
/// K06「永世执念」的回合结束惩罚标记：挂在目标角色身上，玩家侧回合结束时令其失去 4 点力量。
/// </summary>
[RegisterPower]
public class SilverKeyOathPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/UI/KeyOrders/90px-永世执念钥令.png",
        BigIconPath: "res://Foreve/Assets/UI/KeyOrders/90px-永世执念钥令.png");

    public override async Task BeforeSideTurnEnd(
        PlayerChoiceContext ctx,
        CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        if (Owner == null || Owner.IsDead) return;

        await PowerCmd.Apply<StrengthPower>(ctx, Owner, -4, Owner, null, false);
        await PowerCmd.Remove(this);
    }
}

/// <summary>
/// K14「蚀骨的拥抱」的下回合格挡：目标角色下个玩家回合开始时获得 9 点格挡，随后移除。
/// </summary>
[RegisterPower]
public class SilverKeyNextTurnBlockPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/UI/KeyOrders/90px-蚀骨的拥抱钥令.png",
        BigIconPath: "res://Foreve/Assets/UI/KeyOrders/90px-蚀骨的拥抱钥令.png");

    public override async Task AfterSideTurnStart(
        CombatSide side,
        IReadOnlyList<Creature> sideCreatures,
        ICombatState combatState)
    {
        // 双人局中主/副角色共享玩家侧回合：按「下一玩家侧回合开始」结算，目标选谁都能稳定触发。
        if (side != CombatSide.Player) return;
        if (Owner == null || Owner.IsDead) return;

        await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Move, null, false);
        await PowerCmd.Remove(this);
    }
}
