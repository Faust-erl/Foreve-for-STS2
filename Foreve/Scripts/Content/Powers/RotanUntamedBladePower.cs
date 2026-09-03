using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers;

[RegisterPower]
public class RotanUntamedBladePower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/rotan_untamed_blade.png",
        BigIconPath: "res://Foreve/Assets/Powers/rotan_untamed_blade_big.png"
    );

    /// <summary>回响进行中标记：回响造成的伤害再次触发 AfterDamageGiven 时不再回响（防无限递归）。</summary>
    private static bool _replayingEcho;

    public override async Task AfterDamageGiven(
        PlayerChoiceContext choiceContext,
        Creature dealer,
        DamageResult result,
        ValueProp props,
        Creature target,
        CardModel cardSource)
    {
        if (cardSource == null || !cardSource.Tags.Contains(CardTag.Strike) || dealer != Owner || result.TotalDamage <= 0) return;
        if (_replayingEcho) return;

        _replayingEcho = true;
        try
        {
            for (int i = 0; i < Amount; i++)
            {
                // 随机目标攻击：玩家未选目标（Target 为 null），每段独立随机、允许重复（RebelBlade 同款写法）
                await DamageCmd.Attack(result.TotalDamage)
                    .FromCard(cardSource)
                    .TargetingRandomOpponents(CombatState!, allowDuplicates: true)
                    .Execute(choiceContext);
            }
        }
        finally
        {
            _replayingEcho = false;
        }
    }

    // 「本回合」语义：玩家回合结束时移除
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> sideCreatures)
    {
        if (side != CombatSide.Player) return;
        await PowerCmd.Remove(this);
    }
}
