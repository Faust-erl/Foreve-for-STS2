using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Cards.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierAbsoluteGuard : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(20, ValueProp.Move)
    ];

    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public OgierAbsoluteGuard() : base(baseCost: 2, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, false);

        // 下回合获得格挡（基础 10 / 升级 15）— 用一次性延迟 power，玩家回合开始结算
        await PowerCmd.Apply<OgierNextTurnBlockPower>(choiceContext, Owner.Creature, IsUpgraded ? 15 : 10, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // 升级（绝对守护+）：下回合格挡 10→15（由 OnPlay 按 IsUpgraded 处理）
    }
}

/// <summary>
/// 绝对守护的下回合格挡：玩家下个回合开始时获得 Amount 点格挡，随后移除。
/// </summary>
[RegisterPower]
public class OgierNextTurnBlockPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        // 复用已有图标（未生成专属图标前不引用不存在的资源）
        IconPath: "res://Foreve/Assets/Powers/ogier_hold_ground_end_turn.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_hold_ground_end_turn_big.png"
    );

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player.Creature != Owner) return;

        await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Move, null, false);
        await PowerCmd.Remove(this);
    }
}
