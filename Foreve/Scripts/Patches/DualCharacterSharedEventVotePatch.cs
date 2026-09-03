using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using Foreve.Scripts.DualCharacter;
using Logger = MegaCrit.Sts2.Core.Logging.Logger;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：合作事件（Shared Event）投票同步。
///
/// 根因（EventSynchronizer 反编译实证）：
///   - 合作事件是联机投票制。UI 只让 LocalContext.GetMe（双人局恒为主玩家）调
///     ChooseLocalOption → PlayerVotedForSharedOptionIndex(LocalPlayer, ...) 写入主玩家票。
///   - 副玩家从不投票 → _playerVotes[副玩家槽] 恒 null → _playerVotes.All(HasValue)
///     永远不满足 → 合作事件卡在「等待其他玩家投票」。
///
/// 方案：
///   - Postfix 拦截 EventSynchronizer.PlayerVotedForSharedOptionIndex：
///     当写入的是主玩家票且当前是合作事件时，立即用同一 optionIndex/pageIndex 替副玩家
///     再投一票。副玩家票写入后 _playerVotes.All 满足，主机侧正常选路并继续事件。
///   - 因为双人局只有两名玩家，主玩家票先写入时副玩家仍为 null，不会提前触发
///     ChooseSharedEventOption；副玩家镜像票写入后才触发一次，无重复选路。
///   - 仅双人模式生效；单玩家/真联机多人零变化。
///
/// ⚠️ 接线（主流程统一接 Entry.cs，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterSharedEventVotePatch.Install(Logger);
/// </summary>
public static class DualCharacterSharedEventVotePatch
{
    private static Logger _logger = null!;

    /// <summary>EventSynchronizer.PlayerVotedForSharedOptionIndex（private，反射调用补投副玩家票）。</summary>
    private static MethodInfo? _playerVotedForSharedOptionIndex;

    public static void Install(Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_shared_event_vote");

        var voteMethod = AccessTools.DeclaredMethod(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex");
        if (voteMethod == null)
        {
            _logger.Warn("[Foreve][Dual] EventSynchronizer.PlayerVotedForSharedOptionIndex NOT FOUND - skip shared event vote mirror");
            return;
        }

        _playerVotedForSharedOptionIndex = voteMethod;

        harmony.Patch(voteMethod, postfix: new HarmonyMethod(
            typeof(DualCharacterSharedEventVotePatch).GetMethod(nameof(PlayerVotedForSharedOptionIndexPostfix),
                BindingFlags.Static | BindingFlags.NonPublic)));

        _logger.Info("[Foreve][Dual] shared event vote mirror installed (dual-mode secondary follows main)");
    }

    /// <summary>主玩家在合作事件中投票后，替副玩家投同一票。</summary>
    private static void PlayerVotedForSharedOptionIndexPostfix(
        EventSynchronizer __instance,
        Player player,
        uint optionIndex,
        uint pageIndex)
    {
        try
        {
            if (!DualCharacterState.Enabled) return;
            var main = DualCharacterState.MainPlayer;
            var secondary = DualCharacterState.SecondaryPlayer;
            if (main == null || secondary == null || !ReferenceEquals(player, main)) return;
            if (!__instance.IsShared) return;
            if (_playerVotedForSharedOptionIndex == null) return;

            // 直接调用同一私有方法写入副玩家票；内部会触发 PlayerVoteChanged 刷新 UI，
            // 并在双人局两票齐全后由主机侧正常选择合作事件选项。
            _playerVotedForSharedOptionIndex.Invoke(__instance, new object[] { secondary, optionIndex, pageIndex });
            _logger?.Info($"[Foreve][Dual] shared event vote mirrored for secondary player (option {optionIndex}, page {pageIndex})");
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] shared event vote mirror failed: {e.Message}");
        }
    }
}
