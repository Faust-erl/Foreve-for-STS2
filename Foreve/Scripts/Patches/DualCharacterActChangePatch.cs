using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using Foreve.Scripts.DualCharacter;
using Logger = MegaCrit.Sts2.Core.Logging.Logger;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：击败每层 Boss 后的换层投票同步。
///
/// 根因（IL 1068811-1068965 实证）：
///   - 奖励界面点击继续时 NRewardsScreen 调 ActChangeSynchronizer.SetLocalPlayerReady，
///     它只把 LocalContext.GetMe（双人局恒为主玩家）的 VoteToMoveToNextActAction 入队。
///   - 副玩家从不投票 → _readyPlayers[副玩家槽] 恒 false →
///     IsWaitingForOtherPlayers 恒 true，界面显示「等待其他玩家」并卡死。
///
/// 方案：
///   1. SetLocalPlayerReady Postfix：主玩家投票后，原子补投副玩家一票
///      （VoteToMoveToNextActAction 用副玩家 Player 构造，入同一个 ActionQueueSynchronizer）。
///      OnPlayerReady 收到两票后 _readyPlayers.All=true → MoveToNextAct 正常触发。
///   2. IsWaitingForOtherPlayers Prefix：双人模式直接返回 false，
///      「等待其他玩家」遮罩不再出现（避免补票执行前的瞬间闪遮罩）。
/// 仅双人模式生效；单玩家/真联机多人零变化。
///
/// ⚠️ 接线（主流程统一接 Entry.cs，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterActChangePatch.Install(Logger);
/// </summary>
public static class DualCharacterActChangePatch
{
    private static Logger _logger = null!;

    public static void Install(Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_act_change");

        var setReady = AccessTools.DeclaredMethod(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.SetLocalPlayerReady));
        if (setReady == null)
        {
            _logger.Warn("[Foreve][Dual] ActChangeSynchronizer.SetLocalPlayerReady NOT FOUND - skip act change vote mirror");
            return;
        }
        harmony.Patch(setReady, postfix: new HarmonyMethod(
            typeof(DualCharacterActChangePatch).GetMethod(nameof(SetLocalPlayerReadyPostfix),
                BindingFlags.Static | BindingFlags.NonPublic)));

        var isWaiting = AccessTools.DeclaredMethod(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.IsWaitingForOtherPlayers));
        if (isWaiting != null)
        {
            harmony.Patch(isWaiting, prefix: new HarmonyMethod(
                typeof(DualCharacterActChangePatch).GetMethod(nameof(IsWaitingForOtherPlayersPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
        }

        _logger.Info($"[Foreve][Dual] act change vote mirror installed " +
                     $"(SetLocalPlayerReady={setReady != null}, IsWaitingForOtherPlayers={isWaiting != null})");
    }

    /// <summary>主玩家投出换层票后，替副玩家投同一票（副玩家槽 ready=true）。</summary>
    private static void SetLocalPlayerReadyPostfix()
    {
        try
        {
            if (!DualCharacterState.Enabled) return;
            var main = DualCharacterState.MainPlayer;
            var secondary = DualCharacterState.SecondaryPlayer;
            if (main == null || secondary == null) return;

            var manager = RunManager.Instance;
            var queue = manager?.ActionQueueSynchronizer;
            if (queue == null) return;

            // 与 SetLocalPlayerReady 原方法同款：把 VoteToMoveToNextActAction 入队。
            queue.RequestEnqueue(new VoteToMoveToNextActAction(secondary));
            _logger?.Info("[Foreve][Dual] act change vote mirrored for secondary player");
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] act change vote mirror failed: {e.Message}");
        }
    }

    /// <summary>双人模式不显示「等待其他玩家」遮罩（副玩家票由 Postfix 原子补投）。</summary>
    private static bool IsWaitingForOtherPlayersPrefix(ref bool __result)
    {
        if (!DualCharacterState.Enabled) return true;
        __result = false;
        return false;
    }
}
