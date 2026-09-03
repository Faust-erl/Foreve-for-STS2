using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：宝箱遗物只出一份供主玩家选择（2026-08-16 用户实测修复）。
///
/// 根因（C:\tmp\sts2_full.il 实证）：
///   TreasureRoomRelicSynchronizer.BeginRelicPicking 会遍历 runState.Players：
///     1. 为每名玩家创建一个 PlayerVote；
///     2. Hook.ShouldGenerateTreasure(runState, player) 为 true 的每名玩家各从共享
///        RelicGrabBag 抽一个遗物 → 双人局出现 2 个遗物；
///     3. 单机/伪多人且 Players.Count &gt; 1 时，给所有非本地玩家随机投一票
///        （IL 1082693-1082725）→ 副角色被自动分配一个遗物。
///   旧的 DoExtraRewardsIfNeeded 收敛只覆盖战斗后额外奖励，不覆盖宝箱遗物本体。
///
/// 修复（方法级，无 IL 编号依赖）：
///   a) Hook.ShouldGenerateTreasure Prefix：双人模式副玩家返回 false → 只抽 1 个遗物；
///   b) BeginRelicPicking Postfix：把副玩家的 PlayerVote 改成「已跳过」
///      （voteReceived=true, index=null）—— OnPicked 等待全部 voteReceived 才能发奖，
///      而 AwardRelics 的兜底分配会排除已跳过的玩家 → 副角色不会拿遗物；
///   c) 防御：若 _currentRelics 仍 &gt; 1（其他 mod/版本漂移），只保留主玩家对应那一份；
///   d) 重新触发 VotesChanged，让投票 UI 按修正后的状态刷新。
///   真联机多人 / 单玩家局零变化。
///
/// ⚠️ 接线：Entry.cs 调用 Foreve.Scripts.Patches.DualCharacterTreasurePatch.Install(Logger);
/// </summary>
public static class DualCharacterTreasurePatch
{
    private static MegaCrit.Sts2.Core.Logging.Logger? _logger;

    private static readonly FieldInfo PlayerCollectionField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection");
    private static readonly FieldInfo VotesField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes");
    private static readonly FieldInfo CurrentRelicsField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_currentRelics");
    private static readonly FieldInfo VotesChangedField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "VotesChanged");

    private static readonly FieldInfo VoteIndexField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer.PlayerVote), "index");
    private static readonly FieldInfo VoteReceivedField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer.PlayerVote), "voteReceived");

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_treasure");

        var shouldGenerate = AccessTools.Method(typeof(Hook), "ShouldGenerateTreasure",
            new[] { typeof(IRunState), typeof(Player) });
        harmony.Patch(shouldGenerate,
            prefix: new HarmonyMethod(GetMethod(nameof(ShouldGenerateTreasurePrefix))));

        var beginRelicPicking = AccessTools.Method(typeof(TreasureRoomRelicSynchronizer),
            nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking), Type.EmptyTypes);
        harmony.Patch(beginRelicPicking,
            postfix: new HarmonyMethod(GetMethod(nameof(BeginRelicPickingPostfix))));

        _logger?.Info($"[Foreve][Dual] 宝箱遗物单份 patch 已装 (ShouldGenerateTreasure={shouldGenerate != null}, BeginRelicPicking={beginRelicPicking != null}, " +
                      $"字段 PlayerCollection={PlayerCollectionField != null}, Votes={VotesField != null}, CurrentRelics={CurrentRelicsField != null}, " +
                      $"VotesChanged={VotesChangedField != null}, VoteIndex={VoteIndexField != null}, VoteReceived={VoteReceivedField != null})");
    }

    /// <summary>双人模式副玩家不生成宝箱遗物/宝箱金币（全部资源归主玩家）。</summary>
    [HarmonyPriority(Priority.First)]
    private static bool ShouldGenerateTreasurePrefix(IRunState runState, Player player, ref bool __result)
    {
        try
        {
            if (!DualCharacterState.Enabled || player == null) return true;
            if (!DualCharacterState.IsSecondaryPlayer(player)) return true;
            __result = false;
            return false;
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] ShouldGenerateTreasure 前缀异常: {e.Message}");
            return true;
        }
    }

    /// <summary>
    /// BeginRelicPicking 完成后修正投票状态：
    ///   副玩家 = 已跳过（voteReceived=true, index=null）→ 不阻塞发奖，也不会被分配遗物；
    ///   遗物列表只保留主玩家一份（防御其他 mod 让副玩家也生成的情况）。
    /// </summary>
    private static void BeginRelicPickingPostfix(TreasureRoomRelicSynchronizer __instance)
    {
        try
        {
            if (!DualCharacterState.Enabled || __instance == null) return;

            var players = PlayerCollectionField?.GetValue(__instance) as IPlayerCollection;
            var votes = VotesField?.GetValue(__instance) as IList;
            var relics = CurrentRelicsField?.GetValue(__instance) as IList;
            if (players == null || players.Players == null || players.Players.Count < 2) return;

            var main = DualCharacterState.MainPlayer ?? players.Players[0];
            var secondary = DualCharacterState.SecondaryPlayer;

            // 1) 副玩家标记为跳过（不选遗物，也不被 AwardRelics 兜底分配）。
            if (secondary != null && votes != null)
            {
                var slot = players.GetPlayerSlotIndex(secondary);
                if (slot >= 0 && slot < votes.Count && votes[slot] != null)
                {
                    VoteIndexField?.SetValue(votes[slot], null);
                    VoteReceivedField?.SetValue(votes[slot], true);
                    Godot.GD.Print($"[Foreve][Dual][Treasure] 副玩家遗物投票已标记为跳过 (slot={slot})");
                }
            }

            // 2) 防御：万一仍有 2 个遗物，只保留主玩家对应顺序那份。
            if (relics != null && relics.Count > 1)
            {
                var mainSlot = players.GetPlayerSlotIndex(main);
                var keepIndex = mainSlot >= 0 && mainSlot < relics.Count ? mainSlot : 0;
                for (var i = relics.Count - 1; i >= 0; i--)
                {
                    if (i != keepIndex) relics.RemoveAt(i);
                }
                Godot.GD.Print($"[Foreve][Dual][Treasure] 宝箱遗物收敛为 1 份 (keepIndex={keepIndex})");
            }

            // 3) 让已订阅的投票 UI 按修正后的状态刷新一次。
            (VotesChangedField?.GetValue(__instance) as Action)?.Invoke();
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] BeginRelicPicking 后缀异常: {e.Message}");
        }
    }

    private static MethodInfo GetMethod(string name)
        => typeof(DualCharacterTreasurePatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
}
