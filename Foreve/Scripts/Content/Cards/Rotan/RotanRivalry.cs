using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanRivalry : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(7, ValueProp.Move)
    ];

    public RotanRivalry() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Common, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target!;

        // 驱散负面状态：基础各随机驱散 1 项；升级（颉颃+）驱散所有
        await DispelDebuffs(choiceContext, Owner.Creature, IsUpgraded);
        await DispelDebuffs(choiceContext, target, IsUpgraded);

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this)
            .Targeting(target)
            .Execute(choiceContext);
    }

    /// <summary>
    /// 驱散目标身上的负面状态：disperseAll=false 时随机驱散 1 项，true 时全部驱散。
    /// </summary>
    private static async Task DispelDebuffs(PlayerChoiceContext choiceContext, Creature creature, bool disperseAll)
    {
        var debuffs = creature.Powers.Where(p => p.Type == PowerType.Debuff).ToList();
        if (debuffs.Count == 0) return;

        if (disperseAll)
        {
            foreach (var debuff in debuffs)
            {
                await PowerCmd.Remove(debuff);
            }
        }
        else
        {
            var pick = debuffs[new Random().Next(debuffs.Count)];
            await PowerCmd.Remove(pick);
        }
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        // 升级（颉颃+）：驱散所有负面状态（由 OnPlay 按 IsUpgraded 处理）
    }
}
