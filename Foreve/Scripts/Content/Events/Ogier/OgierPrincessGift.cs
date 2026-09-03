using System.Linq;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.Characters.Ogier;
using Foreve.Scripts.Content.Cards.Ogier;
using Foreve.Scripts.Content.Relics.Ogier;

namespace Foreve.Scripts.Content.Events.Ogier;

[RegisterSharedEvent]
public class OgierPrincessGift : ModEventTemplate
{
    public override EventAssetProfile AssetProfile => new(
        InitialPortraitPath: "res://Foreve/Assets/Events/ogier_princess_gift.png"
    );

    public override bool IsAllowed(IRunState runState)
    {
        // 奥吉尔专属事件：队伍必须包含奥吉尔；保留原有 Act2+荣誉≥4 条件
        if (!runState.Players.Any(p => p.Character is OgierCharacter)) return false;
        if (runState.CurrentActIndex < 1) return false;
        if (runState is not RunState rs) return false;
        return OgierCharacter.HasMaxHonorReached(rs, 4);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions() =>
    [
        new EventOption(this, TakeMedal, InitialOptionKey("TAKE_MEDAL")),
        new EventOption(this, Refuse, InitialOptionKey("REFUSE")),
        new EventOption(this, Kneel, InitialOptionKey("KNEEL")),
    ];

    private async Task TakeMedal()
    {
        await RelicCmd.Obtain<OgierGloryMedal>(Owner!);
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.TAKE_MEDAL.description"));
    }

    private async Task Refuse()
    {
        await CreatureCmd.LoseMaxHp(new ThrowingPlayerChoiceContext(), Owner!.Creature, 5, false);
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.REFUSE.description"));
    }

    private async Task Kneel()
    {
        await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), Owner!.Creature, 15, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        var card = Owner.RunState.CreateCard<OgierSevenArtsVirtue>(Owner);
        await RewardsCmd.OfferCustom(Owner, [new SpecialCardReward(card, Owner)]);
        SetEventFinished(L10NLookup($"{Id.Entry}.pages.KNEEL.description"));
    }
}
