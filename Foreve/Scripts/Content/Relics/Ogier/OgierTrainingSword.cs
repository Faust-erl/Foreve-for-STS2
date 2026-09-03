using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Content.Relics.Ogier;

[RegisterRelic(typeof(Characters.Ogier.OgierRelicPool))]
public class OgierTrainingSword : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Common;

    public override RelicAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Relics/Ogier/OgierTrainingSword.png",
        IconOutlinePath: "res://Foreve/Assets/Relics/Ogier/OgierTrainingSword_outline.png",
        BigIconPath: "res://Foreve/Assets/Relics/Ogier/OgierTrainingSword_big.png"
    );

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> sideCreatures)
    {
        if (side != CombatSide.Player) return;
        // 荣誉读奥吉尔所属玩家（主/副无关），遗物 owner 在双人局已合并到主玩家。
        var honorPlayer = DualCharacterRelicScoping.ResolveOgierPlayer(Owner);
        var honor = SecondaryResourceCmd.Get(honorPlayer, Characters.Ogier.OgierCharacter.HonorResourceId);
        if (honor < 3) return;

        // 2026-08-15 新规格：遗物对所有卡牌适用 → 弃牌堆中任意角色的牌都可随机升级
        // （双人局弃牌堆已合并为主玩家；单玩家模式全为奥吉尔牌，行为不变）。
        var discard = Owner.PlayerCombatState.DiscardPile.Cards.ToList();
        if (discard.Count == 0) return;

        var random = new Random();
        var card = discard[random.Next(discard.Count)];
        CardCmd.Upgrade(card, CardPreviewStyle.None);
    }
}
