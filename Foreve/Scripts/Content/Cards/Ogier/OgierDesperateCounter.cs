using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Cards.DynamicVars;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierDesperateCounter : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];

    public OgierDesperateCounter() : base(baseCost: 2, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.AnyEnemy, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var missingHp = Owner.Creature.MaxHp - Owner.Creature.CurrentHp;

        await CreatureCmd.GainBlock(Owner.Creature, missingHp, ValueProp.Move, cardPlay, false);

        await DamageCmd.Attack(missingHp)
            .FromCard(this)
            .Targeting(cardPlay.Target!)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        // 升级后获得保留（Retain）——游戏 Tags/Keywords 缓存不随 IsUpgraded 刷新，须在 OnUpgrade 动态添加
        AddKeyword(CardKeyword.Retain);
    }
}
