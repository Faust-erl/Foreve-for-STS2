using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Characters.Ogier;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Content.Potions.Ogier;

[RegisterPotion(typeof(Characters.Ogier.OgierPotionPool))]
public class OgierKnightToughness : ModPotionTemplate
{
    public override PotionRarity Rarity => PotionRarity.Uncommon;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.AnyPlayer;

    public override PotionAssetProfile AssetProfile => new(
        ImagePath: "res://Foreve/Assets/Potions/Ogier/OgierKnightToughness.png",
        OutlinePath: "res://Foreve/Assets/Potions/Ogier/OgierKnightToughness_outline.png"
    );

    protected override async Task OnUse(PlayerChoiceContext choiceContext, Creature target)
    {
        // target is always Creature in potions；格挡给玩家选择的 target（与药水 TargetType.AnyPlayer 一致）
        await CreatureCmd.GainBlock(target, 12, ValueProp.Move, null, false);

        // 荣誉入账到奥吉尔所属玩家（主/副无关），药水 owner 在双人局已合并到主玩家。
        await SecondaryResourceCmd.Gain(
            DualCharacterRelicScoping.ResolveOgierPlayer(Owner),
            OgierCharacter.HonorResourceId,
            1,
            this);
    }
}
