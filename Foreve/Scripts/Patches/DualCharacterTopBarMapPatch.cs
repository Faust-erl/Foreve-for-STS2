using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using Foreve.Scripts.DualCharacter;
using Logger = MegaCrit.Sts2.Core.Logging.Logger;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：地图投票同步（批次 1b 实测问题修复）。
/// （顶栏双血条部分已于 2026-08-15 移除——用户拍板：副玩家血量改由原生联机缩略条承载，
///  NMultiplayerPlayerStateContainer 隐藏逻辑已反转为「只隐藏主玩家面板」，见 DualCharacterUiPolishPatch。）
///
/// ── 地图双头像同步（Prefix MapSelectionSynchronizer.PlayerVotedForMapCoord）──
/// 根因（IL 证据）：
///   - 地图是联机投票制。鼠标点选 NMapPoint → NMapScreen.OnMapPointSelectedLocally
///     （IL 589395）用 GetMe（=主玩家）构造 VoteForMapCoordAction 入 ActionQueueSynchronizer
///     → 执行时调 MapSelectionSynchronizer.PlayerVotedForMapCoord(player, source, destination)
///     （IL 1074899）把票写入 `_votes[slot]` 并抛 PlayerVoteChanged 事件 →
///     NMapScreen.OnPlayerVoteChangedInternal 更新 PlayerVoteDictionary[player] 并刷新
///     NMultiplayerVoteContainer（地图点上按玩家显示的头像/标记）。
///   - 双人局 GetMe 恒返回主玩家 → 只有主玩家的票被写入，副玩家头像停在原地/起点
///     → 「两个头像位置独立，只能操作主玩家路线」。且副玩家票恒为 null 导致全员有票
///     判定（`_votes.All(v=>v.HasValue)`）永远不满足，自动传送被卡死（只能点传送按钮）。
/// 方案：在投票唯一写入口 PlayerVotedForMapCoord 上做 Prefix 原子镜像：
///   - 校验失败（来源/代数不符）return true 放行原方法（原方法同样拒写，双玩家一致）。
///   - 校验通过且是主玩家投票时：主玩家票直接写入 `_votes` 并手动抛 PlayerVoteChanged
///     （UI 即时刷新），再调用真实方法为副玩家落同一票 —— 共识检查只在副玩家写票后
///     执行**恰好一次**，且两票恒相等 → 自动传送目标确定、不会双触发。
///   - 为什么不用 Postfix 补写：主玩家写票后（副玩家仍持旧票）会出现「两票并存」中间态，
///     全员有票判定成立 → 可能随机选票触发一次错误传送，随后副玩家写票再触发一次 → 双传送。
///   - 取消票（destination=null）、换点改票、地图重生成清票（BeforeMapGenerated）都走同一
///     入口，天然覆盖；副玩家票恒与主玩家一致 → 头像/路线同步移动，传送共识一致（同进一房）。
///   仅双人模式生效（Enabled + 主玩家引用判定）；单玩家/真联机多人零变化。
///
/// ⚠️ 接线（主流程统一接 Entry.cs，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterTopBarMapPatch.Install(Logger);
///   （与 Entry.cs 中 DualCharacterUiPolishPatch.Install(Logger) 等并列）
/// </summary>
public static class DualCharacterTopBarMapPatch
{
    private static Logger _logger = null!;

    // MapSelectionSynchronizer 私有成员（Prefix 原子镜像用）
    private static FieldInfo? _votesField;
    private static FieldInfo? _runStateField;
    private static FieldInfo? _acceptingVotesFromSourceField;
    private static FieldInfo? _playerVoteChangedField;

    public static void Install(Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_topbar_map");

        var voteMethod = AccessTools.DeclaredMethod(typeof(MapSelectionSynchronizer), "PlayerVotedForMapCoord");
        if (voteMethod == null)
        {
            _logger.Warn("[Foreve][Dual] MapSelectionSynchronizer.PlayerVotedForMapCoord NOT FOUND - skip map vote mirror");
            return;
        }

        _votesField = AccessTools.Field(typeof(MapSelectionSynchronizer), "_votes");
        _runStateField = AccessTools.Field(typeof(MapSelectionSynchronizer), "_runState");
        _acceptingVotesFromSourceField = AccessTools.Field(typeof(MapSelectionSynchronizer), "_acceptingVotesFromSource");
        _playerVoteChangedField = AccessTools.Field(typeof(MapSelectionSynchronizer), "PlayerVoteChanged");
        if (_votesField == null || _runStateField == null || _acceptingVotesFromSourceField == null
            || _playerVoteChangedField == null)
        {
            _logger.Warn("[Foreve][Dual] MapSelectionSynchronizer private fields NOT FOUND - skip map vote mirror");
            return;
        }

        harmony.Patch(voteMethod, prefix: new HarmonyMethod(
            typeof(DualCharacterTopBarMapPatch).GetMethod(nameof(PlayerVotedForMapCoordPrefix),
                BindingFlags.Static | BindingFlags.NonPublic)));
        _logger.Info("[Foreve][Dual] map vote mirror installed (dual-mode secondary follows main)");
    }

    /// <summary>
    /// 双人模式投票原子镜像（详见类注释）。返回 false 表示本方法已代写主玩家票。
    /// </summary>
    private static bool PlayerVotedForMapCoordPrefix(MapSelectionSynchronizer __instance, Player player,
        MapLocation source, MapVote? destination)
    {
        if (!DualCharacterState.Enabled) return true;
        var main = DualCharacterState.MainPlayer;
        var secondary = DualCharacterState.SecondaryPlayer;
        if (main == null || secondary == null || !ReferenceEquals(player, main)) return true;

        if (_votesField == null || _runStateField == null || _acceptingVotesFromSourceField == null
            || _playerVoteChangedField == null)
        {
            return true; // 反射成员缺失：退化为原逻辑（不镜像）
        }

        try
        {
            // 复刻原方法校验（IL 1074899 IL_0000/IL_0072）：来源必须是当前收票位置
            var accepting = (MapLocation)_acceptingVotesFromSourceField.GetValue(__instance);
            if (source != accepting) return true; // 原方法会 warn+拒写，放行即可（双玩家保持一致）
            // 代数校验：旧地图的票直接拒写（与原方法一致）
            if (destination.HasValue && destination.Value.mapGenerationCount < __instance.MapGenerationCount)
                return true;

            var runState = (RunState)_runStateField.GetValue(__instance);
            if (runState == null) return true;
            var votes = (List<MapVote?>)_votesField.GetValue(__instance);
            if (votes == null) return true;

            var mainSlot = runState.GetPlayerSlotIndex(main);
            var secSlot = runState.GetPlayerSlotIndex(secondary);
            if (mainSlot < 0 || mainSlot >= votes.Count || secSlot < 0 || secSlot >= votes.Count) return true;

            // 原子写：主玩家直接落票 + 手动抛事件（NMapScreen UI 即时刷新）
            var oldMain = votes[mainSlot];
            votes[mainSlot] = destination;
            (_playerVoteChangedField.GetValue(__instance) as Action<Player, MapVote?, MapVote?>)
                ?.Invoke(main, oldMain, destination);

            // 副玩家走真实方法落票（校验会再次通过；全员有票 → 恰好一次自动传送）
            __instance.PlayerVotedForMapCoord(secondary, source, destination);
            return false; // 主玩家的写入已在上方完成，跳过原方法
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] map vote mirror failed, fallback vanilla: {e.Message}");
            return true; // 任何异常退回原逻辑（宁可不同步，不可破坏原流程）
        }
    }
}
