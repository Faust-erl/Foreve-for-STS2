using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
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
public class RotanPoisonSting : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(4, ValueProp.Move)
    ];

    public RotanPoisonSting() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Common, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 先挂一次性 Power：攻击结算的 AfterDamageGiven 阶段按 TotalDamage（含力量加成、不扣格挡）给毒。
        // 必须先行——AttackCommand.Execute 返回 builder 自身拿不到 DamageResult（IL 实证），
        // 且 AfterDamageGiven 在 Execute 内部已分发完，Power 后挂就收不到本次攻击。
        await PowerCmd.Apply<RotanPoisonStingPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);

        var target = cardPlay.Target!;
        var dmg = DynamicVars.Damage.BaseValue;
        await DamageCmd.Attack(dmg)
            .FromCard(this)
            .Targeting(target)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
    }
}

/// <summary>
/// 毒刺的一次性上毒 Power（原版 EnvenomPower/ReaperFormPower 同款 AfterDamageGiven 范式）：
/// 只对毒刺本卡打出的伤害结算后按 TotalDamage 给予等量中毒，随后自毁。
/// </summary>
[RegisterPower]
public class RotanPoisonStingPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerAssetProfile AssetProfile => new(
        // 毒刺暂无专属图标，复用 rotan_strike_replay 占位（pck 已存在；专属图生成后再替换）
        IconPath: "res://Foreve/Assets/Powers/rotan_strike_replay.png",
        BigIconPath: "res://Foreve/Assets/Powers/rotan_strike_replay_big.png"
    );

    public override async Task AfterDamageGiven(
        PlayerChoiceContext choiceContext,
        Creature dealer,
        DamageResult result,
        ValueProp props,
        Creature target,
        CardModel cardSource)
    {
        // 只给毒刺本卡打出的伤害上毒——回响/其他来源的伤害 cardSource 不是毒刺，不给毒
        if (cardSource is not RotanPoisonSting) return;
        if (result.TotalDamage <= 0) return;
        await PowerCmd.Apply<PoisonPower>(choiceContext, target, result.TotalDamage, Owner, null, false);
        await PowerCmd.Remove(this);
    }
}
