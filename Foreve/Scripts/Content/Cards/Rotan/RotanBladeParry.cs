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
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanBladeParry : ModCardTemplate
{
    public RotanBladeParry() : base(baseCost: 2, type: CardType.Attack, rarity: CardRarity.Uncommon, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    // 升级后增加「保留」（Retain）——游戏 Keywords 缓存不随 IsUpgraded 刷新，在 OnUpgrade 动态添加
    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new DamageVar(9, ValueProp.Move)
    ];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var target = cardPlay.Target!;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this)
            .Targeting(target)
            .Execute(choiceContext);
        // 偷甲：先打伤害（伤害已扣除目标格挡），再读目标**当前剩余**格挡，等量转移给自己（PiercingSpear 同款 LoseBlock）
        var stolen = target.Block;
        if (stolen > 0)
        {
            await CreatureCmd.LoseBlock(target, stolen);
            await CreatureCmd.GainBlock(Owner.Creature, stolen, ValueProp.Move, cardPlay, false);
        }
    }

    protected override void OnUpgrade()
    {
        // 升级不再降伤：伤害保持 9 点（去掉 Damage.BaseValue = 6）
        // 升级的实际变化 = 增加 Retain 关键字（游戏 Keywords 缓存不随 IsUpgraded 刷新，须在 OnUpgrade 动态添加）
        AddKeyword(CardKeyword.Retain);
    }
}
