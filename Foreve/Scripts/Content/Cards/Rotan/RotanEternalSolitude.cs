using Godot;
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
public class RotanEternalSolitude : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    public RotanEternalSolitude() : base(baseCost: 0, type: CardType.Skill, rarity: CardRarity.Uncommon, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target!;
        await PowerCmd.Apply<WeakPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
        await PowerCmd.Apply<WeakPower>(choiceContext, target, 2, Owner.Creature, null, false);

        // 升级（悠远孤寂+）：目标额外获得 2 层易伤
        if (IsUpgraded)
        {
            GD.Print($"[Foreve] 悠远孤寂+ 施加易伤 2 层 目标={target.GetType().Name}");
            await PowerCmd.Apply<VulnerablePower>(choiceContext, target, 2, Owner.Creature, null, false);
        }
    }

    protected override void OnUpgrade()
    {
        // 升级效果（目标额外获得 2 层易伤）在 OnPlay 中按 IsUpgraded 处理
    }
}
