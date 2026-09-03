using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
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
public class RotanRecklessWave : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png",
        VisualStyle: CardVisualStyle.Ancient
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(5, ValueProp.Move)
    ];

    protected override HashSet<CardTag> CanonicalTags => [CardTag.Strike, RotanTags.RotanStrikeTag];

    public RotanRecklessWave() : base(baseCost: 1, type: CardType.Attack, rarity: CardRarity.Ancient, target: TargetType.AllEnemies, showInCardLibrary: true)
    {
        // 注意：BaseReplayCount 不能在构造器设置——卡牌构造器创建的是 canonical 原型，
        // 而 setter 内部 AssertMutable()（canonical 上抛 CanonicalModelException，游戏启动即崩）。
        // 基础重放次数在 AfterCloned（mutable 克隆后回调）里设置。
    }

    /// <summary>mutable 实例克隆完成后设置基础重放 1 次（此回调时实例已是 mutable，setter 可用）。</summary>
    protected override void AfterCloned()
    {
        base.AfterCloned();
        BaseReplayCount = 1;
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
        // 重放 N 次由 BaseReplayCount 驱动（游戏自动整卡重放，含伤害与力量）
    }

    protected override void OnUpgrade()
    {
        // 升级实例是 mutable，setter 可用：重放 1→2（描述自动渲染"重放 2"词条）
        BaseReplayCount = 2;
    }
}
