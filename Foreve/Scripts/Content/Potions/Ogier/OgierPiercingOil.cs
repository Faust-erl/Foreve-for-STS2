using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Potions;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Content.Powers.Ogier;

namespace Foreve.Scripts.Content.Potions.Ogier;

[RegisterPotion(typeof(Characters.Ogier.OgierPotionPool))]
public class OgierPiercingOil : ModPotionTemplate
{
    public override PotionRarity Rarity => PotionRarity.Rare;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.AnyPlayer;

    public override PotionAssetProfile AssetProfile => new(
        ImagePath: "res://Foreve/Assets/Potions/Ogier/OgierPiercingOil.png",
        OutlinePath: "res://Foreve/Assets/Potions/Ogier/OgierPiercingOil_outline.png"
    );

    protected override async Task OnUse(PlayerChoiceContext choiceContext, Creature target)
    {
        // target is Creature, work directly
            await PowerCmd.Apply<OgierPiercingOilPower>(choiceContext, Owner.Creature, 1, Owner.Creature, null, false);
    }
}
