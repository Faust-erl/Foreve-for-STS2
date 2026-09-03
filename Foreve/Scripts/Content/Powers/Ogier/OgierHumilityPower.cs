using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Powers.Ogier;

[RegisterPower]
public class OgierHumilityPower : ModPowerTemplate
{
    private bool _attackPlayedThisTurn;

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;

    public override PowerAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Powers/ogier_humility.png",
        BigIconPath: "res://Foreve/Assets/Powers/ogier_humility_big.png"
    );

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        _attackPlayedThisTurn = false;
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card.Type == CardType.Attack && cardPlay.Card.Owner?.Creature == Owner)
            _attackPlayedThisTurn = true;
    }

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> sideCreatures)
    {
        if (side != CombatSide.Player) return;
        if (_attackPlayedThisTurn) return;
        // Owner is always Player, skip check

        var player = Owner.Player;
        if (player == null) return;
        var blockAmount = Amount > 1 ? 8m : 5m;
        await SecondaryResourceCmd.Gain(player, Characters.Ogier.OgierCharacter.HonorResourceId, 1, this);
        await CreatureCmd.GainBlock(Owner, blockAmount, ValueProp.Move, null, false);
    }
}
