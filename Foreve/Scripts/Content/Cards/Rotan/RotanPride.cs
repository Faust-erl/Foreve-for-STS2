using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
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
public class RotanPride : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(15, ValueProp.Move)
    ];

    public RotanPride() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (Owner.Creature.Block == 0)
        {
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, -1, Owner.Creature, null, false);
            // 升级（傲慢+）：本回合伤害变为三倍（2 层倍伤 = Amount 2 → 3x）
            await PowerCmd.Apply<RotanDoubleDamagePower>(choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null, false);
        }
        else
        {
            await CreatureCmd.GainBlock(Owner.Creature, (decimal)(IsUpgraded ? 20 : 15), ValueProp.Move, cardPlay, false);
            await PowerCmd.Apply<DexterityPower>(choiceContext, Owner.Creature, -1, Owner.Creature, null, false);
        }
    }

    protected override void OnUpgrade()
    {
        // 3x damage instead of 2x, 20 block instead of 15
    }
}

[RegisterPower]
public class RotanDoubleDamagePower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/rotan_double_damage.png",
        BigIconPath: "res://Foreve/Assets/Powers/rotan_double_damage_big.png"
    );

    public override Decimal ModifyDamageMultiplicative(Creature target, Decimal amount, ValueProp props, Creature dealer, CardModel cardSource)
    {
        if (dealer != Owner) return 1;
        return Amount + 1; // 2x at 1 stack, 3x at 2 stacks
    }

    // 「本回合」语义：玩家回合结束时倍伤失效（RotanStrikeReplayPower 同款模板）
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        await PowerCmd.Remove(this);
    }
}
