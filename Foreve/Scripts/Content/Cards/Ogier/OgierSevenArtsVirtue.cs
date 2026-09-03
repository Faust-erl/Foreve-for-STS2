using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using STS2RitsuLib.Cards.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Ogier;

[RegisterCard(typeof(Characters.Ogier.OgierCardPool))]
public class OgierSevenArtsVirtue : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Ogier/{GetType().Name}.png"
    );

    protected override IEnumerable<DynamicVar> CanonicalVars => [
        new BlockVar(15, ValueProp.Move)
    ];

    public OgierSevenArtsVirtue() : base(baseCost: 3, type: CardType.Skill, rarity: CardRarity.Rare, target: TargetType.Self, showInCardLibrary: true) { }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        // 驱散所有负面状态
        var debuffs = Owner.Creature.Powers.Where(p => p.Type == PowerType.Debuff).ToList();
        foreach (var debuff in debuffs)
        {
            await PowerCmd.Remove(debuff);
        }

        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay, false);

        var strAmount = IsUpgraded ? 4 : (int)3;
        await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, strAmount, Owner.Creature, null, false);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(5);
        // strength 3→4 handled in OnPlay
    }
}
