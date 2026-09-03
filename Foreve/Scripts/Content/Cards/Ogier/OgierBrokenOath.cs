using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using Foreve.Scripts.Content.Powers.Ogier;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierBrokenOath : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    public override HashSet<CardKeyword> CanonicalKeywords => [];

    public OgierBrokenOath() : base(baseCost: -1, type: CardType.Curse, rarity: CardRarity.Curse, target: TargetType.Self, showInCardLibrary: true) { }

    // 诅咒牌不可升级（与 RotanOath 同款写法）
    public override int MaxUpgradeLevel => 0;

    protected override bool IsPlayable => false;

    // 诅咒牌不可打出：牌库中的此牌实例本身是 hook listener（RunState.IterateHookListeners 遍历
    // Player.Deck.Cards，IL 实证），每场战斗开始时把「回合结束扣格挡」效果接上（OgierBrokenOathPower）。
    public override async Task BeforeCombatStart()
    {
        if (Owner == null || Owner.Creature == null) return;
        await PowerCmd.Apply<OgierBrokenOathPower>(new ThrowingPlayerChoiceContext(), Owner.Creature, 1, Owner.Creature, null, false);
    }
}
