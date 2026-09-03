using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Potions.Rotan;

[RegisterPotion(typeof(Characters.Rotan.RotanPotionPool))]
public class RotanInexhaustibleFervor : ModPotionTemplate
{
    public override PotionRarity Rarity => PotionRarity.Uncommon;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.AnyPlayer;

    public override PotionAssetProfile AssetProfile => new(
        ImagePath: "res://Foreve/Assets/Potions/RotanInexhaustibleFervor.png",
        OutlinePath: "res://Foreve/Assets/Potions/RotanInexhaustibleFervor_outline.png"
    );

    protected override async Task OnUse(PlayerChoiceContext choiceContext, Creature target)
    {
        await PowerCmd.Apply<StrengthPower>(choiceContext, target, 1, target, null, false);
        await CardPileCmd.Draw(choiceContext, 1, Owner);
    }
}
