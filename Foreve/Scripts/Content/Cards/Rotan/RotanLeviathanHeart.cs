using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Models.Capabilities;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanLeviathanHeart : ModCardTemplate
{
    public RotanLeviathanHeart() : base(baseCost: 6, type: CardType.Power, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true)
    {
        // 挂载动态减费能力：本局（本场战斗）每打出过一张打击/攻击牌，此牌耗能 -1。
        // RitsuLib patch 的 CardEnergyCost.GetWithModifiers 会遍历卡牌能力里的
        // ICardEnergyCostContributor（含手牌费用显示与可打出检查），费用下限 0 由 RitsuLib 统一钳制。
        // 注：NuGet 0.5.11 缺 AddCapability 扩展方法，用 ModelCapabilities.Get(model).Apply(...) 等价入口（桀骜之刃同款）。
        ModelCapabilities.Get(this).Apply(new LeviathanHeartCostCapability());
        // 触发打击计数器的静态构造（在模型注册期完成生命周期订阅，确保战斗开始前已就绪）
        _ = LeviathanHeartStrikeTracker.StrikesPlayedThisCombat;
    }

    // 关键字：基础=保留（Retain）；升级=保留 + 固有（Innate，游戏 Keywords 缓存不随 IsUpgraded 刷新，在 OnUpgrade 动态添加）
    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];

    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 保持原效果：给予「攻击牌均视为打击」能力（原版无动态加 Strike 标签机制，此 Power 不扩展全局标签）
        await PowerCmd.Apply<RotanAllAttacksStrikePower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // 升级后额外获得固有（Innate）
        AddKeyword(CardKeyword.Innate);
    }

    /// <summary>
    /// 利维坦之心动态减费能力：按本局（本场战斗）已打出的打击/攻击牌数量降低当前费用。
    /// </summary>
    private sealed class LeviathanHeartCostCapability : CardCapability, ICardEnergyCostContributor
    {
        public int ModifyEnergyCost(CardModel card, int currentCost, CostModifiers modifiers)
        {
            return currentCost - LeviathanHeartStrikeTracker.StrikesPlayedThisCombat;
        }
    }
}

/// <summary>
/// 利维坦之心减费统计：统计本局（本场战斗）玩家打出的「打击或攻击牌」数量。
/// 与混沌之兽同款「本局累计」语义：只在战斗开始清零，不在回合开始清零（跨战斗不残留）。
/// 计数规则（按代码事实）：
/// - 只统计 Player 拥有的牌（CardModel.Owner != null，敌方牌 Owner 为 null）；
/// - 只统计「有 Strike 标签 OR CardType.Attack」的牌（攻击牌视为打击——最小实现：在 Hook 里直接判，不全局改标签）；
/// - 利维坦之心必须正在手牌中才累计（CardModel.get_Pile() == Hand），匹配描述「此牌在手牌中时」。
/// </summary>
internal static class LeviathanHeartStrikeTracker
{
    private static int _strikesPlayedThisCombat;

    public static int StrikesPlayedThisCombat => _strikesPlayedThisCombat;

    static LeviathanHeartStrikeTracker()
    {
        // 打击/攻击牌结算完成后计数
        RitsuLibFramework.SubscribeLifecycle<CardPlayedEvent>(e =>
        {
            var card = e.CardPlay.Card;
            if (card == null || card.Owner == null) return; // 只统计玩家打出的牌
            // 此牌在手牌中时才累计（描述：「此牌在手牌中时，你每次使用打击后此牌耗能-1」）
            if (!card.Owner.PlayerCombatState.Hand.Cards.Any(c => c is RotanLeviathanHeart)) return;
            // 打击（CardTag.Strike）或攻击牌（CardType.Attack）都算
            if (!RotanTags.IsStrikeCard(card) && card.Type != CardType.Attack) return;
            _strikesPlayedThisCombat++;
        }, replayCurrentState: false);

        // 战斗开始兜底清零（本局 = 本场战斗累计，跨战斗不残留）
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(_ => _strikesPlayedThisCombat = 0, replayCurrentState: false);
    }

    /// <summary>战斗结束清零：防止跨战斗残留导致战斗外（牌库/奖励/地图）费用显示仍按上一场计数扣减。
    /// 由 CombatCostReset 统一调用。</summary>
    internal static void ResetAfterCombat() => _strikesPlayedThisCombat = 0;
}

[RegisterPower]
public class RotanAllAttacksStrikePower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/rotan_all_attacks_strike.png",
        BigIconPath: "res://Foreve/Assets/Powers/rotan_all_attacks_strike_big.png"
    );
}
