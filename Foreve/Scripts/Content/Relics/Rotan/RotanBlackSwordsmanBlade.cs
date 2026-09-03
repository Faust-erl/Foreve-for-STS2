using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using STS2RitsuLib.Cards;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.Scaffolding.Content;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Content.Relics.Rotan;

[RegisterRelic(typeof(Characters.Rotan.RotanRelicPool))]
[RegisterCharacterStarterRelic(typeof(Characters.Rotan.RotanCharacter))]
public class RotanBlackSwordsmanBlade : ModRelicTemplate, ICardOnPlayHookListener
{
    public override RelicRarity Rarity => RelicRarity.Starter;

    public override RelicAssetProfile AssetProfile => new(
        IconPath: "res://Foreve/Assets/Relics/Rotan/RotanBlackSwordsmanBlade.png",
        IconOutlinePath: "res://Foreve/Assets/Relics/Rotan/RotanBlackSwordsmanBlade_outline.png",
        BigIconPath: "res://Foreve/Assets/Relics/Rotan/RotanBlackSwordsmanBlade_big.png"
    );

    private bool _strikePlayedThisTurn;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        _strikePlayedThisTurn = false;
    }

    // 单玩家模式：原版 hook（owner 打出时触发）。2026-08-15 新规格：遗物对
    // 所有卡牌适用 —— 任意打击（不限角色）都触发抽 1。
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (DualCharacterState.Enabled) return; // 双人模式走战斗级全量 hook（下），避免双重触发
        if (!_strikePlayedThisTurn && cardPlay.Card.Tags.Contains(CardTag.Strike))
        {
            _strikePlayedThisTurn = true;
            await CardPileCmd.Draw(choiceContext, 1, Owner);
        }
    }

    /// <summary>
    /// 双人模式：RitsuLib 战斗级全量 hook（谁打出的牌都收到，与遗物 owner 无关）。
    /// 2026-08-15 新规格：遗物对两名角色以及所有卡牌适用 —— 萝坦/奥吉尔（或原版角色）
    /// 的任意打击牌均触发抽 1。卡牌 _owner 已合并为主玩家，抽牌按主玩家牌堆执行。
    /// </summary>
    public async Task AfterCardOnPlay(AfterCardOnPlayContext context)
    {
        if (!DualCharacterState.Enabled) return;
        if (_strikePlayedThisTurn) return;
        if (!context.CardPlay.Card.Tags.Contains(CardTag.Strike)) return;

        _strikePlayedThisTurn = true;
        await CardPileCmd.Draw(context.ChoiceContext, 1, Owner);
    }
}
