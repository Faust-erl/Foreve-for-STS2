using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：开局注入第二个玩家（批次 1a）。
///
/// Harmony Prefix 拦截 RunState.CreateForNewRun —— 单/多人两条开局链的公共汇聚点。
/// （实证：NGame.StartNewSingleplayerRun d__141 与 StartNewMultiplayerRun d__142/选人 d__56
/// 最终都调用这个同步 static 方法；IL 172072-172138，返回 RunState，非 async。）
///
/// 注入方式：
///   - 单人链传入的是 <>z__ReadOnlySingleElementList&lt;Player&gt;（不可变，不能 Add）；
///     多人链传入的是 List&lt;Player&gt;。统一处理：构造 List{主, 副} 后**递归调用**
///     CreateForNewRun（Count==2 时前缀直接放行，不会二次注入）。
///   - 副玩家 = Player.CreateForNewRun(SecondCharacter, 主玩家.UnlockState, 主玩家.NetId + 1)。
///     UnlockState 用主玩家公开属性（单人=GenerateUnlockStateFromProgress 的结果，
///     多人=主机 UnlockState.FromSerializable 的结果）；NetId+1 保证 ≠ 主玩家 NetId，
///     通过 RunManager.CanonicalizeSave 的"Players 必须包含本地玩家 NetId"校验。
///   - Player.PopulateStartingInventory 要求 RunState 为 NullRunState —— 前缀在
///     CreateForNewRun 创建 RunState 之前创建副玩家，天然满足。
///
/// ⚠️ 接线（主流程在 Entry.cs 统一处理，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterRunStartPatches.Install(Logger);
/// </summary>
public static class DualCharacterRunStartPatches
{
    private static bool _injecting;

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        var harmony = new Harmony("foreve.dual_character_run_start");

        var target = AccessTools.Method(typeof(RunState), "CreateForNewRun", new[]
        {
            typeof(IReadOnlyList<Player>),
            typeof(IReadOnlyList<ActModel>),
            typeof(IReadOnlyList<ModifierModel>),
            typeof(GameMode),
            typeof(int),
            typeof(string),
        });
        if (target == null)
        {
            logger.Warn("[Foreve][Dual] RunState.CreateForNewRun NOT FOUND —— 双角色开局注入不可用");
            return;
        }

        harmony.Patch(target, prefix: new HarmonyMethod(
            typeof(DualCharacterRunStartPatches).GetMethod(nameof(CreateForNewRunPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
        logger.Info("[Foreve][Dual] RunState.CreateForNewRun 前缀已安装（双角色第二玩家注入）");
    }

    private static bool CreateForNewRunPrefix(
        ref RunState __result,
        IReadOnlyList<Player> players,
        IReadOnlyList<ActModel> acts,
        IReadOnlyList<ModifierModel> modifiers,
        GameMode gameMode,
        int ascensionLevel,
        string seed)
    {
        try
        {
            if (!DualCharacterState.Enabled) return true;
            var secondCharacter = DualCharacterState.SecondCharacter;
            if (secondCharacter == null) return true;
            if (players == null || players.Count != 1) return true;
            if (_injecting) return true;

            _injecting = true;
            try
            {
                var main = players[0];
                var second = Player.CreateForNewRun(
                    secondCharacter,
                    main.UnlockState,
                    main.NetId + 1);
                var list = new List<Player> { main, second };

                GD.Print($"[Foreve][Dual] 注入第二玩家: {second.Character?.Id.Entry} NetId={second.NetId} (主={main.Character?.Id.Entry} NetId={main.NetId})");

                // 递归调用：新列表 Count==2，前缀放行 → 原方法体完整执行（CreateShared + InitializeSeed + AfterCreated）
                __result = RunState.CreateForNewRun(list, acts, modifiers, gameMode, ascensionLevel, seed);
                return false; // 跳过原方法（__result 已由递归调用产生）
            }
            finally
            {
                _injecting = false;
            }
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 第二玩家注入失败，回退为单人开局: {e}");
            DualCharacterState.SecondCharacter = null; // 避免后续开局重复尝试
            return true;
        }
    }
}
