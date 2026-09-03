using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
[RegisterCharacterStarterCard(typeof(Characters.Rotan.RotanCharacter), 1)]
public class RotanRebelBlade : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(5, ValueProp.Move),
        // 描述里的「1e」能量图标标记（EnergyVar 默认名 "Energy"，配合 {Energy:energyIcons()} 渲染）
        new EnergyVar(1)
    ];

    public RotanRebelBlade() : base(baseCost: 3, type: CardType.Attack, rarity: CardRarity.Basic, target: TargetType.RandomEnemy, showInCardLibrary: true)
    {
        // 动态减费由 RotanRebelBladeCostPatch 统一实现（PatchAll 自动安装）：
        // CardEnergyCost.GetWithModifiers 的 Postfix 无条件扣除本回合已打出的打击数（下限 0）。
        // ⚠️ 不要再挂 ICardEnergyCostContributor capability —— RitsuLib 的费用补丁在部分上下文
        // 会重复调用，导致一张打击减 2 费（2026-08-24 实测）。
        // 触发打击计数器的静态构造（在模型注册期完成生命周期订阅，确保战斗开始前已就绪）
        _ = RebelBladeStrikeTracker.StrikesPlayedThisTurn;
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 随机目标卡：玩家无需选目标，cardPlay.Target 为 null，不能用 Targeting(cardPlay.Target!).
        // 正确写法（原版 FlakCannon 同款）：WithHitCount(段数) + TargetingRandomOpponents(CombatState, allowDuplicates: true)
        // → Execute 内部每段用战斗目标 RNG（RunState.Rng.CombatTargets）独立随机选 1 个敌人；
        // allowDuplicates=true 允许重复打同一目标（单敌人时 3 段全打它，不会抛 "No valid targets" 异常）。
        var hits = IsUpgraded ? 4 : 3;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .WithHitCount(hits)
            .FromCard(this)
            .TargetingRandomOpponents(CombatState!, allowDuplicates: true)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        // 4 hits instead of 3
    }
}

/// <summary>
/// 桀骜之刃减费统计：统计本回合玩家打出的「打击」牌（rotanstrike 标签或视为打击 CardTag.Strike）数量。
/// 与银钥同款 RitsuLib 生命周期订阅模式：
/// - CardPlayedEvent（Hook.AfterCardPlayed 后发布，牌结算完成后）计数；
/// - SideTurnStartedEvent（玩家回合开始）清零——「本回合」语义；
/// - CombatStartingEvent 兜底清零（防止跨战斗残留计数）。
/// 计数规则（按代码事实）：只统计 Player 拥有的牌（CardModel.Owner != null，敌方牌 Owner 为 null）；
/// 只统计带 rotanstrike 标签或视为打击（CardTag.Strike）的牌；桀骜之刃自身无 Strike 标签，不含自身。
/// 注：「视为打击」的牌（奔浪突袭/升级后的斩断）带 CardTag.Strike，按统一判定计入。
/// </summary>
internal static class RebelBladeStrikeTracker
{
    private static int _strikesPlayedThisTurn;

    public static int StrikesPlayedThisTurn => _strikesPlayedThisTurn;

    static RebelBladeStrikeTracker()
    {
        // 打击牌结算完成后计数；打出一张打击后手牌刷新，桀骜之刃费用即时显示为新值
        RitsuLibFramework.SubscribeLifecycle<CardPlayedEvent>(e =>
        {
            var card = e.CardPlay.Card;
            if (card == null || card.Owner == null) return; // 只统计玩家打出的牌
            if (!RotanTags.IsStrikeCard(card)) return;
            _strikesPlayedThisTurn++;
        }, replayCurrentState: false);

        // 玩家回合开始清零
        RitsuLibFramework.SubscribeLifecycle<SideTurnStartedEvent>(e =>
        {
            if (e.Side == CombatSide.Player)
                _strikesPlayedThisTurn = 0;
        }, replayCurrentState: false);

        // 战斗开始兜底清零
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(_ => _strikesPlayedThisTurn = 0, replayCurrentState: false);
    }

    /// <summary>战斗结束清零：防止跨战斗残留导致战斗外（牌库/奖励/地图）费用显示仍按上一场打击数扣减。
    /// 由 CombatCostReset 统一调用。</summary>
    internal static void ResetAfterCombat() => _strikesPlayedThisTurn = 0;
}
