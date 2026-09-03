using System.Linq;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Characters.Ogier;
using Foreve.Scripts.Content.Cards.Ogier;

namespace Foreve.Scripts.Content.Events.Ogier;

[RegisterSharedEvent]
public class OgierKnightsTrial : ModEventTemplate
{
    public override EventAssetProfile AssetProfile => new(
        InitialPortraitPath: "res://Foreve/Assets/Events/ogier_knights_trial.png"
    );

    // 仅当玩家队伍包含奥吉尔时允许遇到（萝坦队伍不应出现奥吉尔专属事件）
    public override bool IsAllowed(IRunState runState)
    {
        return runState.Players.Any(p => p.Character is OgierCharacter);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        new EventOption(this, TakeSpear, InitialOptionKey("TAKE_SPEAR")),
        new EventOption(this, ReciteHonesty, InitialOptionKey("RECITE_HONESTY")),
        new EventOption(this, Leave, InitialOptionKey("LEAVE")),
    ];

    private async Task TakeSpear()
    {
        await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), Owner!.Creature, 7, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        var card = Owner.RunState.CreateCard<OgierSevenArtsSpear>(Owner);
        await RewardsCmd.OfferCustom(Owner, [new SpecialCardReward(card, Owner)]);
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.TAKE_SPEAR.description"));
    }

    private async Task ReciteHonesty()
    {
        var skills = Owner!.Deck.Cards.Where(c => c.Type == CardType.Skill).ToList();
        if (skills.Count > 0)
        {
            var toRemove = skills[new Random().Next(skills.Count)];
            await CardPileCmd.RemoveFromDeck(toRemove, false);
        }
        var reward = Owner.RunState.CreateCard<OgierHonesty>(Owner);
        await RewardsCmd.OfferCustom(Owner, [new SpecialCardReward(reward, Owner)]);
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.RECITE_HONESTY.description"));
    }

    private async Task Leave()
    {
        await CreatureCmd.Heal(Owner!.Creature, 10, false);
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.LEAVE.description"));
    }
}
