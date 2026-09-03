using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using STS2RitsuLib;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Content.Relics.Dore;

internal static class DoreRelicIcons
{
    public static RelicAssetProfile For(string stem)
    {
        var icon = $"res://Foreve/Assets/Relics/Dore/{stem}.png";
        var outline = $"res://Foreve/Assets/Relics/Dore/{stem}_outline.png";
        var big = $"res://Foreve/Assets/Relics/Dore/{stem}_big.png";

        if (ResourceLoader.Exists(icon))
        {
            return new RelicAssetProfile(
                IconPath: icon,
                IconOutlinePath: ResourceLoader.Exists(outline)
                    ? outline
                    : "res://Foreve/Assets/Relics/Rotan/RotanBlackSwordsmanBlade_outline.png",
                BigIconPath: ResourceLoader.Exists(big)
                    ? big
                    : "res://Foreve/Assets/Relics/Rotan/RotanBlackSwordsmanBlade_big.png"
            );
        }

        return new RelicAssetProfile(
            IconPath: "res://Foreve/Assets/Relics/Rotan/RotanBlackSwordsmanBlade.png",
            IconOutlinePath: "res://Foreve/Assets/Relics/Rotan/RotanBlackSwordsmanBlade_outline.png",
            BigIconPath: "res://Foreve/Assets/Relics/Rotan/RotanBlackSwordsmanBlade_big.png"
        );
    }
}

[RegisterRelic(typeof(Characters.Dore.DoreRelicPool))]
[RegisterCharacterStarterRelic(typeof(Characters.Dore.DoreCharacter))]
public class DoreNutritionTank : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Starter;
    public override RelicAssetProfile AssetProfile => DoreRelicIcons.For("DoreNutritionTank");

    private bool _applied;

    public override async Task BeforeCombatStart()
    {
        _applied = false;
        await Task.CompletedTask;
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (_applied) return;
        if (!DualCharacterState.Enabled && player != Owner) return;

        var equipper = DualCharacterRelicEquip.ResolveEquippedPlayer(this, Owner);
        var target = equipper.Creature;
        if (target == null || target.IsDead) return;

        _applied = true;
        await PowerCmd.Apply<RegenPower>(choiceContext, target, 1, target, null, false);
    }
}

[RegisterRelic(typeof(Characters.Dore.DoreRelicPool))]
public class DoreTechnicalManual : ModRelicTemplate
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;
    public override RelicAssetProfile AssetProfile => DoreRelicIcons.For("DoreTechnicalManual");

    private static bool _activeThisCombat;

    static DoreTechnicalManual()
    {
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(_ => _activeThisCombat = false, replayCurrentState: false);
        RitsuLibFramework.SubscribeLifecycle<CardEnteredCombatEvent>(e =>
        {
            if (_activeThisCombat && e.Card is { Owner: not null, Type: CardType.Attack })
                CardCmd.ApplyKeyword(e.Card, [CardKeyword.Retain]);
        }, replayCurrentState: false);
        RitsuLibFramework.SubscribeLifecycle<CardGeneratedForCombatEvent>(e =>
        {
            if (_activeThisCombat && e.Card is { Owner: not null, Type: CardType.Attack })
                CardCmd.ApplyKeyword(e.Card, [CardKeyword.Retain]);
        }, replayCurrentState: false);
    }

    public override async Task BeforeCombatStart()
    {
        _activeThisCombat = true;

        // 初始进入战斗的卡牌可能在 CardEnteredCombatEvent 之前已入堆，这里再对牌库整体补一遍。
        foreach (var card in Owner.Deck.Cards.ToList())
        {
            if (card.Type == CardType.Attack && !card.Keywords.Contains(CardKeyword.Retain))
                CardCmd.ApplyKeyword(card, [CardKeyword.Retain]);
        }

        await Task.CompletedTask;
    }
}
