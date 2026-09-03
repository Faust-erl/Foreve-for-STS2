using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.Powers;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Content.Cards.Dore;
using Foreve.Scripts.SilverKey;

namespace Foreve.Scripts.Content.Powers.Dore;

internal static class DorePowerIcons
{
    public static PowerAssetProfile For(string stem)
    {
        var icon = $"res://Foreve/Assets/Powers/{stem}.png";
        var big = $"res://Foreve/Assets/Powers/{stem}_big.png";
        if (ResourceLoader.Exists(icon) && ResourceLoader.Exists(big))
            return new PowerAssetProfile(IconPath: icon, BigIconPath: big);

        // 朵尔专属能力图标尚未导入时复用现有占位图标，避免引用不存在的资源。
        return new PowerAssetProfile(
            IconPath: "res://Foreve/Assets/Powers/rotan_strike_replay.png",
            BigIconPath: "res://Foreve/Assets/Powers/rotan_strike_replay_big.png"
        );
    }
}

[RegisterPower]
public class DoreMindAnalysisStrengthLossPower : ModTemporaryAppliedPowerTemplate<DoreMindAnalysis, StrengthPower>
{
    protected override bool IsPositive => false;
    public override PowerAssetProfile AssetProfile => DorePowerIcons.For("dore_mind_analysis");
}

[RegisterPower]
public class DoreMindBodySplitPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => DorePowerIcons.For("dore_mind_body_split");

    public override async Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (power is not RegenPower || power.Owner != Owner || amount == 0m) return;

        // 基础：再生减少时获得格挡；升级（Amount>1）：增加或减少都触发。
        if (amount < 0m || (Amount > 1 && amount > 0m))
            await CreatureCmd.GainBlock(Owner, 6, ValueProp.Move, null, false);
    }
}

[RegisterPower]
public class DorePurityPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => DorePowerIcons.For("dore_purity");

    public override async Task AfterPowerAmountChanged(
        PlayerChoiceContext choiceContext,
        PowerModel power,
        decimal amount,
        Creature? applier,
        CardModel? cardSource)
    {
        if (amount <= 0m) return;
        if (power.Owner != Owner) return;
        if (power is not WeakPower and not VulnerablePower and not FrailPower) return;

        // 后置清除：新施加的虚弱/脆弱/易伤立即被移除，等效于「不受影响」。
        await PowerCmd.Remove(power);
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player.Creature != Owner) return;
        foreach (var power in Owner.Powers
                     .Where(p => p is WeakPower or VulnerablePower or FrailPower)
                     .ToList())
        {
            await PowerCmd.Remove(power);
        }
    }
}

[RegisterPower]
public class DoreBeyondBodyPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => DorePowerIcons.For("dore_beyond_body");

    public override async Task BeforeSideTurnEnd(
        PlayerChoiceContext ctx,
        CombatSide side,
        IEnumerable<Creature> sideCreatures)
    {
        if (side != CombatSide.Player) return;
        if (Owner.Block > 0) return;

        await PowerCmd.Apply<RegenPower>(ctx, Owner, Amount, Owner, null, false);
    }
}

[RegisterPower]
public class DoreNoisyVoicePower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => DorePowerIcons.For("dore_noisy_voice");

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player.Creature != Owner || Amount <= 0) return;
        await CardPileCmd.Draw(ctx, (int)Amount, player);
    }
}

[RegisterPower]
public class DoreJointLubricationPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerAssetProfile AssetProfile => DorePowerIcons.For("dore_joint_lubrication");

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player.Creature != Owner || Amount <= 0) return;

        await PowerCmd.Apply<DexterityPower>(ctx, Owner, 1, Owner, null, false);
        await PowerCmd.Apply<DoreJointLubricationPower>(ctx, Owner, -1, Owner, null, false);
        if (Amount <= 0)
            await PowerCmd.Remove(this);
    }
}

[RegisterPower]
public class DoreSilverKeyReactorPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => DorePowerIcons.For("dore_silver_key_reactor");

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player.Creature != Owner) return;

        var current = SecondaryResourceCmd.Get(player, SilverKeyResource.ResourceId);
        if (current <= 0) return;

        var spent = await SecondaryResourceCmd.Spend(player, SilverKeyResource.ResourceId, 1, null, this);
        if (spent)
            await CardPileCmd.Draw(ctx, 1, player);
    }
}

[RegisterPower]
public class DoreSilverKeyRadiationPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerAssetProfile AssetProfile => DorePowerIcons.For("dore_silver_key_radiation");

    static DoreSilverKeyRadiationPower()
    {
        // 用全局监听器接银钥消耗事件，再查当前持有辐射能力的玩家。
        SecondaryResourceHook.RegisterGlobalListener(new RadiationGlobalListener());
    }

    private sealed class RadiationGlobalListener : ISecondaryResourceHookListener
    {
        public async Task AfterSecondaryResourceSpent(SecondaryResourceSpendContext ctx)
        {
            if (ctx.Definition.Id != SilverKeyResource.ResourceId) return;

            var power = ctx.Player.Creature?.Powers.OfType<DoreSilverKeyRadiationPower>().FirstOrDefault();
            if (power == null || power.Amount <= 0) return;

            var enemies = ctx.CombatState.Enemies.Where(e => !e.IsDead).ToList();
            if (enemies.Count == 0) return;

            var damage = (int)power.Amount;
            var choiceCtx = new ThrowingPlayerChoiceContext();
            for (var i = 0; i < ctx.Amount; i++)
            {
                foreach (var enemy in enemies)
                {
                    await CreatureCmd.Damage(choiceCtx, enemy, damage, ValueProp.Move, power.Owner, null);
                }
            }
        }
    }
}
