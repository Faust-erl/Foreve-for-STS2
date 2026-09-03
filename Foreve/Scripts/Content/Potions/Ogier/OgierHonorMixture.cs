using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Potions;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Characters.Ogier;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Content.Potions.Ogier;

[RegisterPotion(typeof(Characters.Ogier.OgierPotionPool))]
public class OgierHonorMixture : ModPotionTemplate
{
    public override PotionRarity Rarity => PotionRarity.Common;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.AnyPlayer;

    public override PotionAssetProfile AssetProfile => new(
        ImagePath: "res://Foreve/Assets/Potions/Ogier/OgierHonorMixture.png",
        OutlinePath: "res://Foreve/Assets/Potions/Ogier/OgierHonorMixture_outline.png"
    );

    protected override async Task OnUse(PlayerChoiceContext choiceContext, Creature target)
    {
        // target is Creature, work directly
        // 荣誉入账到奥吉尔所属玩家（主/副无关），药水 owner 在双人局已合并到主玩家。
        await SecondaryResourceCmd.Gain(
            DualCharacterRelicScoping.ResolveOgierPlayer(Owner),
            OgierCharacter.HonorResourceId,
            2,
            this);
    }
}
