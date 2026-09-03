using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanUnquenchableFervor : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    public RotanUnquenchableFervor() : base(baseCost: 1, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.AnyPlayer, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<RotanStrikeReplayPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // 升级（战意难平+）：费用 1→0
        EnergyCost.UpgradeBy(-1);
    }
}

[RegisterPower]
public class RotanStrikeReplayPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    // 「本回合内」语义：层数无意义，Single 覆盖（每回合只需存在一次）
    public override PowerStackType StackType => PowerStackType.Single;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/rotan_strike_replay.png",
        BigIconPath: "res://Foreve/Assets/Powers/rotan_strike_replay_big.png"
    );

    /// <summary>重放进行中标记：重放产生的 CardPlayed 事件不再触发二次重放（防无限循环）。</summary>
    private static bool _replaying;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card == null || cardPlay.Card.Owner == null) return; // 只重放玩家打出的牌
        if (!RotanTags.IsStrikeCard(cardPlay.Card)) return;
        if (_replaying) return; // 重放的打击结算后再打出，不再重放

        _replaying = true;
        try
        {
            // 重放同一张打击：Target==null（随机目标打击）时传 null 由 RitsuLib 处理随机目标
            // （CardCmdAutoPlayAnyPlayerPatch），保证不崩且语义完整
            await CardCmd.AutoPlay(choiceContext, cardPlay.Card, cardPlay.Target, AutoPlayType.Default, skipXCapture: true, skipCardPileVisuals: false);
        }
        finally
        {
            _replaying = false;
        }
    }

    // 「本回合」语义：玩家回合结束时移除
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> sideCreatures)
    {
        if (side != CombatSide.Player) return;
        await PowerCmd.Remove(this);
    }
}
