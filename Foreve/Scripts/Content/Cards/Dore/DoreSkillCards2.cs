using System.Linq;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Content.Powers.Dore;

namespace Foreve.Scripts.Content.Cards.Dore;

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DorePersistentSpirit : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(5, ValueProp.Move)
    ];

    public DorePersistentSpirit() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true)
    {
        // 在模型注册期触发追踪器静态构造，确保首张上一张牌也能被记录。
        DorePersistentSpiritTracker.Touch();
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, false);

        // 「上一张牌」：获得本牌打出前最后一张由玩家使用的牌的复制。
        var previous = DorePersistentSpiritTracker.GetPreviousCard(this);
        if (previous == null || ReferenceEquals(previous, this)) return;

        var runState = Owner.RunState as RunState;
        if (runState == null) return;

        var copy = DoreCardHelpers.CreateCardCopy(runState, previous, Owner);
        if (copy == null) return;

        CardCmd.ApplyKeyword(copy, [CardKeyword.Exhaust]);
        await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, Owner, CardPilePosition.Top);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
    }
}

/// <summary>
/// 存续精神「上一张牌」追踪：记录玩家最近打出的两张牌。
/// CardPlayingEvent 与 OnPlay 的先后顺序兼容两种可能：
/// 事件已发布时返回前一张；事件尚未发布时当前记录就是前一张。
/// 战斗开始清零，跨战斗不残留。
/// </summary>
internal static class DorePersistentSpiritTracker
{
    private static CardModel? _currentCard;
    private static CardModel? _previousCard;

    public static void Touch()
    {
    }

    public static CardModel? GetPreviousCard(CardModel currentCard)
    {
        return ReferenceEquals(_currentCard, currentCard) ? _previousCard : _currentCard;
    }

    static DorePersistentSpiritTracker()
    {
        RitsuLibFramework.SubscribeLifecycle<CardPlayingEvent>(e =>
        {
            var card = e.CardPlay.Card;
            if (card == null || card.Owner == null) return; // 只记录玩家打出的牌
            _previousCard = _currentCard;
            _currentCard = card;
        }, replayCurrentState: false);

        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(_ =>
        {
            _currentCard = null;
            _previousCard = null;
        }, replayCurrentState: false);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreMindAnalysis : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreMindAnalysis() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.AllEnemies, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var amount = IsUpgraded ? 5 : 3;
        foreach (var enemy in CombatState!.Enemies.Where(e => !e.IsDead).ToList())
        {
            await PowerCmd.Apply<DoreMindAnalysisStrengthLossPower>(
                choiceContext, enemy, amount, enemy, null, false);
        }
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreGestalt : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => BuildCanonicalKeywords();

    private int _temporaryCostBonus;

    public DoreGestalt() : base(baseCost: 0, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    private static HashSet<CardKeyword> BuildCanonicalKeywords()
    {
        var set = new HashSet<CardKeyword> { CardKeyword.Retain };
        // 游戏枚举可能不包含 Eternal；有则添加（不硬编码，避免不同游戏版本编译/运行风险）。
        try
        {
            if (Enum.TryParse<CardKeyword>("Eternal", out var eternal))
                set.Add(eternal);
        }
        catch
        {
            // 无 Eternal 关键字时，本地化描述里仍保留「永恒」文本。
        }
        return set;
    }

    public void SetTemporaryCostBonus(int bonus)
    {
        if (bonus <= 0) return;
        _temporaryCostBonus = bonus;
        EnergyCost.UpgradeBy(bonus);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CardPileCmd.Draw(choiceContext, 1, Owner);

        // 生成的复制在当回合费用 +1；若复制再次打出，则继续生成更贵的复制（能量自然限制链条）。
        var copy = Owner.Creature?.CombatState?.CreateCard<DoreGestalt>(Owner)
            ?? Owner.RunState.CreateCard<DoreGestalt>(Owner);
        var bonus = DoreCardHelpers.GetSortCost(this) + 1;
        copy.SetTemporaryCostBonus(bonus);
        await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, Owner, CardPilePosition.Top);
    }

    public override async Task BeforeSideTurnEnd(
        PlayerChoiceContext ctx,
        MegaCrit.Sts2.Core.Combat.CombatSide side,
        IEnumerable<Creature> participants)
    {
        if (side != MegaCrit.Sts2.Core.Combat.CombatSide.Player) return;
        if (_temporaryCostBonus <= 0) return;

        // 「仅在本回合费用+1」：回合结束时撤销临时加费。
        EnergyCost.UpgradeBy(-_temporaryCostBonus);
        _temporaryCostBonus = 0;
    }

    /// <summary>战斗结束：撤销仍未还原的「仅在本回合费用+1」（战斗在回合结束前结束/复制牌打出后
    /// 未触发回合结束撤销时的残留）。由 CombatCostReset 统一调用。</summary>
    internal void ResetCombatCostState()
    {
        if (_temporaryCostBonus <= 0) return;
        EnergyCost.UpgradeBy(-_temporaryCostBonus);
        _temporaryCostBonus = 0;
    }

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Innate);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreReorder : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreReorder() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var state = Owner.PlayerCombatState;
        var all = state.Hand.Cards
            .Concat(state.DrawPile.Cards)
            .Concat(state.DiscardPile.Cards)
            .OrderBy(DoreCardHelpers.GetSortCost)
            .ToList();

        // 用高层 CardPileCmd 跨牌堆移动，确保每张卡的当前牌堆引用被正确更新；
        // 先全部集中到弃牌堆，再按费用升序放入抽牌堆底部（低费在上），避免低层 Clear/AddInternal 导致手牌被误判为消耗。
        await CardPileCmd.Add(all, PileType.Discard, CardPilePosition.Bottom, null, true);
        await CardPileCmd.Add(all, PileType.Draw, CardPilePosition.Bottom, null, true);

        await CardPileCmd.Draw(choiceContext, 4, Owner);
    }

    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreSynapticAdaptation : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(9, ValueProp.Move)
    ];

    private bool _costReducedThisCombat;

    public DoreSynapticAdaptation() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Common, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, false);

        if (!_costReducedThisCombat)
        {
            _costReducedThisCombat = true;
            EnergyCost.UpgradeBy(-1);
        }
    }

    /// <summary>战斗结束：清除「本场已减费」标记（费用本身由 CombatCostReset 统一还原到战斗开始基准），
    /// 保证下一场战斗可再次触发「本场战斗中此牌费用-1」。由 CombatCostReset 统一调用。</summary>
    internal void ResetCombatCostState()
    {
        _costReducedThisCombat = false;
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreWillRemnant : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(6, ValueProp.Move)
    ];

    public DoreWillRemnant() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, false);
        await PowerCmd.Apply<ArtifactPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2);
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreEmergencySuture : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public DoreEmergencySuture() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.AnyPlayer, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.Heal(cardPlay.Target ?? Owner.Creature, IsUpgraded ? 8 : 5, false);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreThoughtSlice : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreThoughtSlice() : base(baseCost: 0, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CardPileCmd.Draw(choiceContext, 1, Owner);

        var hand = Owner.PlayerCombatState.Hand.Cards.ToList();
        if (hand.Count == 0) return;

        var prefs = new CardSelectorPrefs(
            new LocString("foreve_I18N_cards", "FOREVE_CARD_SELECT_THOUGHT_SLICE_HAND"),
            Math.Min(1, hand.Count), 1)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };

        var selected = await CardSelectCmd.FromHand(choiceContext, Owner, prefs, null, this);
        var card = selected.FirstOrDefault();
        if (card == null) return;

        var isBad = card.Type is CardType.Status or CardType.Curse;
        if (IsUpgraded && isBad)
        {
            await CardPileCmd.Add(card, PileType.Exhaust);
        }
        else
        {
            await CardPileCmd.Add(card, PileType.Discard);
        }

        if (isBad)
            await CardPileCmd.Draw(choiceContext, 1, Owner);
    }

    protected override void OnUpgrade()
    {
    }
}

[RegisterCard(typeof(Characters.Dore.DoreCardPool))]
public class DoreReverseEncoding : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Dore/{GetType().Name}.png"
    );

    public DoreReverseEncoding() : base(baseCost: 0, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.AnyPlayer, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target ?? Owner.Creature;
        var self = Owner.Creature;

        var strength = self.Powers.OfType<StrengthPower>().FirstOrDefault()?.Amount ?? 0m;
        var dexterity = self.Powers.OfType<DexterityPower>().FirstOrDefault()?.Amount ?? 0m;

        if (strength != 0m)
        {
            await PowerCmd.Apply<StrengthPower>(choiceContext, target, strength, target, null, false);
            if (!IsUpgraded)
                await PowerCmd.Apply<StrengthPower>(choiceContext, self, -strength, self, null, false);
        }

        if (dexterity != 0m)
        {
            await PowerCmd.Apply<DexterityPower>(choiceContext, target, dexterity, target, null, false);
            if (!IsUpgraded)
                await PowerCmd.Apply<DexterityPower>(choiceContext, self, -dexterity, self, null, false);
        }
    }

    protected override void OnUpgrade()
    {
    }
}
