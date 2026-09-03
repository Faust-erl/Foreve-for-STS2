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
public class RotanReunion : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    public RotanReunion() : base(baseCost: 3, type: CardType.Power, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // Infuse — handled by vanilla Infuse system
        await PowerCmd.Apply<DexterityPower>(choiceContext, Owner.Creature, IsUpgraded ? 3 : 2, Owner.Creature, null, false);
        await CardPileCmd.Draw(choiceContext, IsUpgraded ? 2 : 1, Owner);
    }

    // 注能「每场战斗开始自动打出」：原版 Imbued 附魔仅接受技能牌（CanEnchantCardType 只放行
    // Skill，能力牌 Enchant 会抛异常），改用卡自身无参 BeforeCombatStart hook——牌库卡实例本身
    // 就是 hook listener（RunState.IterateHookListeners 遍历 Player.Deck.Cards，IL 实证）。
    // ThrowingPlayerChoiceContext 为无 ctx hook 里执行命令的安全选择（原版遗物同款用法）。
    public override async Task BeforeCombatStart()
    {
        if (Owner == null || Owner.Creature == null) return;
        await CardCmd.AutoPlay(new ThrowingPlayerChoiceContext(), this, null, AutoPlayType.Default, true, false);
    }

    protected override void OnUpgrade()
    {
        // +3 dex, draw 2
    }
}
