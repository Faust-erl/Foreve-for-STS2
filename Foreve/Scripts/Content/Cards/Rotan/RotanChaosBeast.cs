using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
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
[RegisterDustyTomeCard(typeof(Characters.Rotan.RotanCharacter))]
public class RotanChaosBeast : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png",
        VisualStyle: CardVisualStyle.Ancient
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(6, ValueProp.Move)
    ];

    public RotanChaosBeast() : base(baseCost: 3, type: CardType.Attack, rarity: CardRarity.Ancient, target: TargetType.AnyEnemy, showInCardLibrary: true)
    {
        // 触发打击计数器的静态构造（在模型注册期完成生命周期订阅，确保战斗开始前已就绪）
        _ = ChaosBeastStrikeTracker.StrikesPlayedThisCombat;
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target!;
        var dmg = IsUpgraded ? 9 : 6;
        // 固定 2 段（升级只变伤害 6→9，段数不变）+ 本局（本场战斗）累计打出过的打击次数段
        var hits = 2 + ChaosBeastStrikeTracker.StrikesPlayedThisCombat;

        for (int i = 0; i < hits; i++)
        {
            await DamageCmd.Attack(dmg)
                .FromCard(this)
                .Targeting(target)
                .Execute(choiceContext);
        }
    }

    protected override void OnUpgrade()
    {
        // Damage 6→9
    }
}

/// <summary>
/// 混沌之兽打击计数：统计本局（本场战斗）玩家累计打出的「打击」牌（rotanstrike 标签或视为打击 CardTag.Strike）数量。
/// 与桀骜之刃同款 RitsuLib 生命周期订阅模式（CardPlayedEvent 计数 / CombatStartingEvent 清零），
/// 但语义为「本局累计」：只在战斗开始清零，不在回合开始清零。
/// 计数规则（按代码事实）：只统计 Player 拥有的牌（CardModel.Owner != null，敌方牌 Owner 为 null）；
/// 只统计带 rotanstrike 标签或视为打击（CardTag.Strike）的牌；混沌之兽自身无 Strike 标签，不含自身。
/// 注：升级后的斩断升级后动态获得 Strike 标签会计入（视为打击）；基础斩断无标签不计入。
/// </summary>
internal static class ChaosBeastStrikeTracker
{
    private static int _strikesPlayedThisCombat;

    public static int StrikesPlayedThisCombat => _strikesPlayedThisCombat;

    static ChaosBeastStrikeTracker()
    {
        // 打击牌结算完成后计数
        RitsuLibFramework.SubscribeLifecycle<CardPlayedEvent>(e =>
        {
            var card = e.CardPlay.Card;
            if (card == null || card.Owner == null) return; // 只统计玩家打出的牌
            if (!RotanTags.IsStrikeCard(card)) return;
            _strikesPlayedThisCombat++;
        }, replayCurrentState: false);

        // 战斗开始兜底清零（本局 = 本场战斗累计，跨战斗不残留）
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(_ => _strikesPlayedThisCombat = 0, replayCurrentState: false);
    }

    /// <summary>战斗结束清零：防止跨战斗残留（本计数影响段数；与费用还原统一处理）。
    /// 由 CombatCostReset 统一调用。</summary>
    internal static void ResetAfterCombat() => _strikesPlayedThisCombat = 0;
}
