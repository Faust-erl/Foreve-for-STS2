using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Scaffolding.Content;

namespace Foreve.Scripts.Content.Cards.Rotan;

[RegisterCard(typeof(Characters.Rotan.RotanCardPool))]
public class RotanOath : ModCardTemplate
{
    public override CardAssetProfile AssetProfile => new(
        PortraitPath: $"res://Foreve/Assets/Cards/Rotan/{GetType().Name}.png"
    );

    private static RunSavedData<OathCombatTracker>? _combatData;

    /// <summary>跨战斗持久化的约定倒计时：记录已进行的战斗场数（获得此牌后开始计数）。</summary>
    public sealed class OathCombatTracker
    {
        public int Value;
    }

    public RotanOath() : base(baseCost: 3, type: CardType.Curse, rarity: CardRarity.Rare, target: TargetType.AnyPlayer, showInCardLibrary: true)
    {
        // 触发每战斗计数标记复位订阅的静态构造（确保战斗开始前已就绪）
        _ = OathCombatCounter.CountedThisCombat;
    }

    public override int MaxUpgradeLevel => 0;

    // 获得此牌后无需打出（诅咒）：牌库中的此牌实例本身是 hook listener
    // （RunState.IterateHookListeners 遍历 Player.Deck.Cards，IL 实证），每场战斗开始都会收到 BeforeCombatStart()。
    public override async Task BeforeCombatStart()
    {
        if (Owner == null) return;
        if (Owner.RunState is not RunState runState) return;

        // 每场战斗只计一次（牌库中多张此牌共享计数；OathCombatCounter 由 RitsuLib CombatStartingEvent
        // 在每次战斗开始时复位标记——该事件是 Hook.BeforeCombatStart 的 Prefix，先于本 hook 触发）
        if (!OathCombatCounter.CountedThisCombat)
        {
            OathCombatCounter.MarkCounted();
            _combatData ??= RunSavedDataStore.For("foreve").Register<OathCombatTracker>("foreve_oath_combats", () => new OathCombatTracker());
            _combatData.Modify(runState, t => t.Value++);
        }

        if (_combatData == null || !_combatData.TryGet(runState, out var tracker)) return;

        if (tracker.Value <= 3)
        {
            // 接下来 3 场战斗开始时失去 1 力量
            await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Owner.Creature, -1, Owner.Creature, null, false);
            return;
        }

        if (tracker.Value == 4)
        {
            // 第 4 场战斗开始：牌库中的此牌变为「重逢」（移除旧的约定 + 生成重逢进入抽牌堆，等价替换 API）
            var reunion = Owner.Creature?.CombatState?.CreateCard<RotanReunion>(Owner)
                ?? Owner.RunState.CreateCard<RotanReunion>(Owner);
            await CardPileCmd.RemoveFromDeck(this, false);
            await CardPileCmd.AddGeneratedCardToCombat(reunion, PileType.Draw, Owner, CardPilePosition.Random);
        }
    }
}

/// <summary>每场战斗只让约定计数一次：RitsuLib CombatStartingEvent 复位标记（Prefix 先于卡牌 hook 触发）。</summary>
internal static class OathCombatCounter
{
    private static bool _countedThisCombat;

    public static bool CountedThisCombat => _countedThisCombat;

    public static void MarkCounted() => _countedThisCombat = true;

    static OathCombatCounter()
    {
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(_ => _countedThisCombat = false, replayCurrentState: false);
    }
}
