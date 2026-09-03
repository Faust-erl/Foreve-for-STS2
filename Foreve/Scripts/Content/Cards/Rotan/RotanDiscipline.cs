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
public class RotanDiscipline : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    public RotanDiscipline() : base(baseCost: 1, type: CardType.Power, rarity: CardRarity.Common, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, -1, Owner.Creature, null, false);
        await PowerCmd.Apply<DexterityPower>(choiceContext, Owner.Creature, 3, Owner.Creature, null, false);

        // 升级（秩锢+）：在下两个回合抽 1 张牌；未升级为在下个回合抽 1 张牌
        await PowerCmd.Apply<RotanDisciplineDrawPower>(choiceContext, Owner.Creature, IsUpgraded ? 2 : 1, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // 升级效果（抽牌回合数 1→2）在 OnPlay 中按 IsUpgraded 处理
    }
}

/// <summary>
/// 秩锢的延迟抽牌：在接下来 N 个回合（Amount 层）开始时各抽 1 张牌，抽完即移除。
/// </summary>
[RegisterPower]
public class RotanDisciplineDrawPower : ModPowerTemplate
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerAssetProfile AssetProfile => new(
        // 复用已有图标（未生成专属图标前不引用不存在的资源）
        IconPath: "res://Foreve/Assets/Powers/rotan_strike_replay.png",
        BigIconPath: "res://Foreve/Assets/Powers/rotan_strike_replay_big.png"
    );

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player.Creature != Owner) return;
        if (Amount <= 0) return;

        await CardPileCmd.Draw(ctx, 1, player);
        // 递减剩余次数：再 Apply 一次 -1（StackType.Counter 叠加）；抽完次数后移除
        await PowerCmd.Apply<RotanDisciplineDrawPower>(ctx, Owner, -1, Owner, null, false);
        if (Amount <= 0)
        {
            await PowerCmd.Remove(this);
        }
    }
}
