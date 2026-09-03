using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Characters.Ogier;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
[RegisterDustyTomeCard(typeof(Characters.Ogier.OgierCharacter))]
public class OgierWorldlyBulwark : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png",
        VisualStyle: CardVisualStyle.Ancient
    );

    public OgierWorldlyBulwark() : base(baseCost: 0, type: CardType.Skill, rarity: CardRarity.Ancient, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var times = IsUpgraded ? 3 : 2;
        for (int i = 0; i < times; i++)
        {
            await CreatureCmd.GainBlock(Owner.Creature, 6, ValueProp.Move, cardPlay, false);
        }

        await SecondaryResourceCmd.Gain(Owner, OgierCharacter.HonorResourceId, 1, this);
    }

    protected override void OnUpgrade() { }
}
