using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;

namespace Foreve.Scripts.DualCharacter;

/// <summary>
/// 双角色模式：RunStartedEvent 资源合并 + RunLoadedEvent 读档状态恢复（批次 1a + 2026-08-16 读档修复）。
///
/// 订阅 RitsuLib RunStartedEvent（replayCurrentState: false；签名见 GameLifecycleContracts.cs:232）：
///   - 仅当 e.RunState.Players.Count == 2 时执行 —— 旧档单玩家局不触发任何逻辑；
///     ⚠️ 不依赖 IsMultiplayer（单人链下它为 false），只用 Players.Count 判断。
///   - 副玩家牌库 → 主玩家：CardPile.AddInternal(card, -1, false) 公开（-1=追加，游戏自身
///     PopulateCombatState 同款）；PopulateStartingDeck 对每个玩家 ToMutable 出独立实例，
///     两玩家卡牌不共享引用，移动安全；移动后副玩家 Deck.Clear(false) 清空。
///   - 金币：主玩家.Gold += 副玩家.Gold；副玩家清零（Player get/set_Gold 均公开）。
///   - 药水：副玩家 _potionSlots 私有 List&lt;PotionModel&gt;（无公开移除 API），反射 Clear()。
///   - 副玩家 MaxEnergy = 0（set_MaxEnergy 公开）—— 共享能量只用主玩家的。
///   - 初始遗物：副玩家遗物搬入主玩家（owner 改写为主玩家，遗物栏全显示；mod 遗物全队/全卡牌
///     生效见 DualCharacterEffects，荣誉等角色专属系统绑定见 DualCharacterRelicScoping）。
/// 合并后消费选择状态（MainCharacter/SecondCharacter 清空，防止 RunState.CreateForNewRun
/// 前缀在后续开局重复注入）；Enabled 保持 true 供 IsDualMode(RunState/ICombatState) 判断。
///
/// ── 2026-08-16 读档修复（用户反馈：保存并退出后重新进入，初始手牌未空、卡组少了起手的 5 张）──
/// 根因：读档走 RunManager.InitializeSavedRun → RitsuLib 发布的是 RunLoadedEvent（不是
/// RunStartedEvent），旧代码只订阅了 RunStartedEvent → 读档后 DualCharacterState.Enabled /
/// MainPlayer / SecondaryPlayer 全部停留在初始值（false/null）→ 所有双人模式战斗 patch
/// （副玩家不抽牌防 ShuffleNRE、自动 ready、GetMe 主玩家、卡牌归属、牌堆重定向、能量共用等）
/// 全部失效 → 读档后的双人战斗退化为「未修复的 2 玩家原始战斗」（副玩家空牌库回合抽牌触发
/// ShuffleNRE、回合卡等待等），表现出手牌/卡组数量异常。
/// 修复：同样订阅 RunLoadedEvent，读档时把 Players.Count == 2 的局重新置为双人模式：
///   - Enabled = true、MainPlayer/SecondaryPlayer 指向 players[0]/[1]；
///   - 幂等地重放一遍合并（正确存档下副玩家牌库/遗物/药水/金币均为空、MaxEnergy=0，
///     全部为无操作；老存档/异常存档可顺带修复，不会重复搬牌）。
/// 注意：读档路径不清空 MainCharacter/SecondCharacter（它们本来就是 null，开局注入前缀
/// 依赖 Enabled && SecondCharacter != null 双重判定，不会误触发）。
///
/// ⚠️ 接线（主流程在 Entry.cs 统一处理，不要直接改 Entry.cs）：
///   Foreve.Scripts.DualCharacter.DualCharacterRunMerge.EnsureInstalled();
/// </summary>
public static class DualCharacterRunMerge
{
    private static bool _installed;

    /// <summary>Player._potionSlots 私有字段（List&lt;PotionModel&gt;）—— 副玩家药水清空用。</summary>
    private static readonly FieldInfo PotionSlotsField = AccessTools.Field(typeof(Player), "_potionSlots");

    /// <summary>CardModel._owner 私有字段 —— 合并时副玩家牌归属改为主玩家（set_Owner 有 "already has an owner" 守卫，必须反射直写）。</summary>
    private static readonly FieldInfo OwnerField = AccessTools.Field(typeof(CardModel), "_owner");

    /// <summary>Player._relics 私有字段（List&lt;RelicModel&gt;）—— 遗物搬移用。</summary>
    private static readonly FieldInfo RelicsField = AccessTools.Field(typeof(Player), "_relics");

    /// <summary>RelicModel._owner 私有字段 —— 搬移时把 owner 改写为主玩家（遗物栏/钩子派发均按主玩家走）。</summary>
    private static readonly FieldInfo RelicOwnerField = AccessTools.Field(typeof(RelicModel), "_owner");

    public static void EnsureInstalled()
    {
        if (_installed) return;
        _installed = true;
        RitsuLibFramework.SubscribeLifecycle<RunStartedEvent>(OnRunStarted, replayCurrentState: false);
        RitsuLibFramework.SubscribeLifecycle<RunLoadedEvent>(OnRunLoaded, replayCurrentState: false);
        GD.Print("[Foreve][Dual] 双角色资源合并/读档恢复已安装 (RunStartedEvent + RunLoadedEvent)");
    }

    private static void OnRunStarted(RunStartedEvent e)
        => ApplyDualCharacterState(e.RunState, isLoad: false);

    private static void OnRunLoaded(RunLoadedEvent e)
        => ApplyDualCharacterState(e.RunState, isLoad: true);

    /// <summary>
    /// 双人模式状态应用（开局合并 / 读档恢复共用）：
    /// 仅 Players.Count == 2 生效；写 Enabled/MainPlayer/SecondaryPlayer 后重放幂等合并。
    /// </summary>
    private static void ApplyDualCharacterState(RunState? run, bool isLoad)
    {
        try
        {
            if (run == null || run.Players.Count != 2) return; // 旧档/非双角色局：不处理

            DualCharacterState.Enabled = true;
            var main = run.Players[0];
            var secondary = run.Players[1];
            DualCharacterState.MainPlayer = main;
            DualCharacterState.SecondaryPlayer = secondary;

            // 幂等合并：正确存档下副玩家牌库/遗物/药水/金币为空、MaxEnergy=0，全部为无操作
            MergeDeck(main, secondary);
            MergeRelics(main, secondary);
            MergeGold(main, secondary);
            ClearSecondaryPotions(secondary);
            secondary.MaxEnergy = 0; // 共享能量只用主玩家的

            GD.Print($"[Foreve][Dual] 双角色{(isLoad ? "读档" : "开局")}状态就绪: 主={main.Character?.Id.Entry} 副={secondary.Character?.Id.Entry} " +
                     $"主牌库={main.Deck.Cards.Count} 主金币={main.Gold} 副牌库={secondary.Deck.Cards.Count} 副能量={secondary.MaxEnergy}");
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][Dual] 双角色{(isLoad ? "读档" : "开局")}状态应用异常: {ex}");
        }
        finally
        {
            if (!isLoad)
            {
                // 选择状态已消费（它是开局注入的触发条件），避免后续 CreateForNewRun 重复注入；
                // 读档路径不动这两个字段（本来就是 null，注入前缀另有 SecondCharacter 判定）
                DualCharacterState.MainCharacter = null;
                DualCharacterState.SecondCharacter = null;
            }
        }
    }

    private static void MergeDeck(Player main, Player secondary)
    {
        var cards = secondary.Deck.Cards.ToList(); // 快照：AddInternal 期间源列表会被 Clear
        foreach (var card in cards)
        {
            main.Deck.AddInternal(card, -1, false);
            // 反射直写 _owner：set_Owner 有 "already has an owner" 守卫（IL 实证 1104431-1104442）。
            // get_Pile/get_CombatState 都从 _owner 推导（IL 1104411/85783110），不改会导致战斗内
            // 克隆牌 Pile/CombatState 查副玩家牌堆失败 → 抽牌 AfterCardEnteredCombat NRE
            // （2026-08-14 探针实证：card=FOREVE_CARD_ROTAN_STRIKE owner=2 pile=null）
            OwnerField?.SetValue(card, main);
        }
        secondary.Deck.Clear(false);
    }

    private static void MergeGold(Player main, Player secondary)
    {
        main.Gold += secondary.Gold;
        secondary.Gold = 0;
    }

    /// <summary>
    /// 副玩家遗物搬入主玩家（遗物栏显示全部遗物，含副角色的初始/专属）。
    /// 副玩家 _relics 逐项移入主玩家 _relics，并把 RelicModel._owner 反射改写为主玩家 →
    /// 遗物栏（NRelicInventory 读主玩家）与遗物钩子派发均按主玩家走。
    /// 2026-08-15 新规格（linker 分支）：mod 遗物（含初始遗物）对两名角色与所有卡牌适用，
    /// 全队效果见 DualCharacterEffects；荣誉等角色专属系统仍由 DualCharacterRelicScoping
    /// 绑定奥吉尔所属玩家。原版遗物不改造（效果作用于主玩家，属已知限制）。
    /// </summary>
    private static void MergeRelics(Player main, Player secondary)
    {
        var mainRelics = RelicsField?.GetValue(main) as List<RelicModel>;
        var secRelics = RelicsField?.GetValue(secondary) as List<RelicModel>;
        if (mainRelics == null || secRelics == null)
        {
            GD.Print("[Foreve][Dual] 遗物合并跳过：_relics 字段获取失败");
            return;
        }

        foreach (var relic in secRelics.ToList())
        {
            RelicOwnerField?.SetValue(relic, main);
            mainRelics.Add(relic);
        }
        secRelics.Clear();
        GD.Print($"[Foreve][Dual] 遗物合并完成（副玩家遗物搬入主玩家，owner 改写为主玩家）: 主遗物={mainRelics.Count}");
    }

    private static void ClearSecondaryPotions(Player secondary)
    {
        var slots = PotionSlotsField?.GetValue(secondary) as List<PotionModel>;
        slots?.Clear();
    }
}
