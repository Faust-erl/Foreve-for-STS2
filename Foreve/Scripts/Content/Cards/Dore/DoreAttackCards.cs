using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.SilverKey;

namespace Foreve.Scripts.Content.Cards.Dore;

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreSoulRip : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(9, ValueProp.Move),
        new BlockVar(9, ValueProp.Move)
    ];

    public DoreSoulRip() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Common, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.Damage(choiceContext, Owner.Creature, 2,
            ValueProp.Unblockable | ValueProp.Unpowered, null, null);

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this)
            .Targeting(cardPlay.Target!)
            .Execute(choiceContext);

        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, false);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
        DynamicVars.Block.UpgradeValueBy(3);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreDisplacementBeta : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreDisplacementBeta() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Common, target: TargetType.RandomEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var amount = SecondaryResourceCmd.Get(Owner, SilverKeyResource.ResourceId);
        if (amount <= 0) return;

        await SecondaryResourceCmd.Spend(Owner, SilverKeyResource.ResourceId, amount, this, this);

        var damagePerPoint = IsUpgraded ? 4 : 3;
        await DamageCmd.Attack(damagePerPoint)
            .WithHitCount(amount)
            .FromCard(this)
            .TargetingRandomOpponents(CombatState!, allowDuplicates: true)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreResort : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(9, ValueProp.Move)
    ];

    public DoreResort() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Uncommon, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this)
            .Targeting(cardPlay.Target!)
            .Execute(choiceContext);

        await DoreCardHelpers.ReorderDrawPileSafelyAsync(
            choiceContext,
            Owner.PlayerCombatState.DrawPile,
            ascending: true);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreOuterSynapse : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreOuterSynapse() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Common, target: TargetType.AllEnemies, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var baseDamage = IsUpgraded ? 9 : 7;
        var extraDamage = IsUpgraded ? 6 : 4;

        await DamageCmd.Attack(baseDamage)
            .FromCard(this)
            .TargetingAllOpponents(CombatState!)
            .Execute(choiceContext);

        foreach (var enemy in CombatState!.Enemies.Where(e => !e.IsDead).ToList())
        {
            if (enemy.CurrentHp > Owner.Creature.CurrentHp)
            {
                await DamageCmd.Attack(extraDamage)
                    .FromCard(this)
                    .Targeting(enemy)
                    .Execute(choiceContext);
            }
        }
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreDecayPulse : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(5, ValueProp.Move)
    ];

    public DoreDecayPulse() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Uncommon, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var regen = Owner.Creature.Powers.OfType<RegenPower>().FirstOrDefault();
        var stacks = (int)(regen?.Amount ?? 0m);
        var perStack = IsUpgraded ? 6 : 5;
        var total = DynamicVars.Damage.BaseValue + stacks * perStack;

        await DamageCmd.Attack(total)
            .FromCard(this)
            .Targeting(cardPlay.Target!)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreSingularitySort : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreSingularitySort() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Uncommon, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var sorted = DoreCardHelpers.IsSortedAscending(Owner.PlayerCombatState.DrawPile.Cards);
        var damage = sorted ? (IsUpgraded ? 18 : 14) : (IsUpgraded ? 11 : 8);

        await DamageCmd.Attack(damage)
            .FromCard(this)
            .Targeting(cardPlay.Target!)
            .Execute(choiceContext);

        if (sorted)
            await CardPileCmd.Draw(choiceContext, 1, Owner);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreLogicCollapse : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreLogicCollapse() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Rare, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var drawPile = Owner.PlayerCombatState.DrawPile;
        var oldOrder = drawPile.Cards.ToList();

        if (IsUpgraded)
            DoreCardHelpers.ReorderDrawPile(drawPile, ascending: false);
        else
            DoreCardHelpers.ShuffleDrawPileAndCountMoved(drawPile);

        var moved = CountMoved(oldOrder, drawPile.Cards.ToList());
        var hits = Math.Max(1, 1 + moved);
        for (var i = 0; i < hits; i++)
        {
            await DamageCmd.Attack(1)
                .FromCard(this)
                .Targeting(cardPlay.Target!)
                .Execute(choiceContext);
        }
    }

    private static int CountMoved(List<CardModel> oldOrder, List<CardModel> newOrder)
    {
        if (oldOrder.Count != newOrder.Count) return Math.Max(oldOrder.Count, newOrder.Count);
        var moved = 0;
        for (var i = 0; i < oldOrder.Count; i++)
        {
            if (!ReferenceEquals(oldOrder[i], newOrder[i]))
                moved++;
        }
        return moved;
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreRegenStorm : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreRegenStorm() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Rare, target: TargetType.AllEnemies, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var regen = Owner.Creature.Powers.OfType<RegenPower>().FirstOrDefault();
        var lost = (int)(regen?.Amount ?? 0m);
        if (regen != null && lost > 0)
        {
            // 走 ModifyAmount 让「再生层数减少」钩子（灵肉两分）正常触发。
            await PowerCmd.ModifyAmount(choiceContext, regen, -lost, Owner.Creature, null, false);
            if (regen.Amount <= 0m)
                await PowerCmd.Remove(regen);
        }

        if (lost <= 0) return;

        var perStack = IsUpgraded ? 12 : 9;
        await DamageCmd.Attack(lost * perStack)
            .FromCard(this)
            .TargetingAllOpponents(CombatState!)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
    }
}
