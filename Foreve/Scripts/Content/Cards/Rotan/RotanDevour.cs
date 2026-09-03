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
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanDevour : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    public RotanDevour() : base(baseCost: 0, type: CardType.Skill, rarity: CardRarity.Common, target: TargetType.AnyPlayer, showInCardLibrary: true) { }

    // 升级（鲸吞+）：获得保留关键字（Retain）——游戏 Keywords 缓存不随 IsUpgraded 刷新，在 OnUpgrade 动态添加
    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var creature = Owner.Creature;

        // 读取敏捷与荆棘层数（原版 power：DexterityPower / ThornsPower）
        var dexterity = creature.Powers.OfType<DexterityPower>().FirstOrDefault();
        var thorns = creature.Powers.OfType<ThornsPower>().FirstOrDefault();
        decimal total = (dexterity?.Amount ?? 0m) + (thorns?.Amount ?? 0m);

        // 失去所有敏捷和荆棘
        if (dexterity != null)
            await PowerCmd.Remove(dexterity);
        if (thorns != null)
            await PowerCmd.Remove(thorns);

        // 获得等量力量
        if (total > 0m)
            await PowerCmd.Apply<StrengthPower>(choiceContext, creature, total, creature, null, false);
    }

    protected override void OnUpgrade()
    {
        // 升级后获得保留关键字（游戏 Keywords 缓存不随 IsUpgraded 刷新，须在 OnUpgrade 动态添加）
        AddKeyword(CardKeyword.Retain);
    }
}
