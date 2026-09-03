using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2RitsuLib.Combat.SecondaryResources;
using Foreve.Scripts.Characters.Ogier;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：奥吉尔荣誉 UI 与角色主/副身份解耦（2026-08-15 实测修复）。
///
/// 问题：荣誉行由 RitsuLib 注册在 NCombatUi 上，但 RitsuLib 只对
/// LocalContext.IsMe(player) 的玩家刷新（GetMe 已被双人 patch 恒返回主玩家）。
/// 当奥吉尔是副角色时：
///   - NCombatUi.Activate 用主玩家刷新 → 荣誉行绑定主玩家 → 0 且隐藏；
///   - SecondaryResourceUiRuntime.UpdateCurrentCombatUi 对副玩家直接 return → 获得荣誉后也不刷新。
///
/// 修复（不新增任何顶栏节点，不动容器布局，避免此前顶栏错位问题）：
///   1. NCombatUi.Activate(CombatState) Postfix：双人局用奥吉尔所属玩家直接刷新树上已有的
///      OgierHonorCounterRow 实例（只刷新荣誉行，不调 UpdateCombatUi 全量刷新——避免银钥行
///      被错误绑定到副玩家）。
///   2. 全局 SecondaryResourceHook 监听：奥吉尔玩家的荣誉变化后同样只刷新荣誉行。
///
/// ⚠️ 接线：Entry.cs 在 DualCharacterCombatPatches 之后调用 Install。
/// </summary>
public static class DualCharacterHonorUiPatch
{
    private static readonly Type NCombatUiType = typeof(NCombatUi);
    private static MegaCrit.Sts2.Core.Logging.Logger? _logger;

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;

        if (!ModSecondaryResourceRegistry.TryGet(OgierCharacter.HonorResourceId, out var honorDef))
        {
            logger.Warn("[Foreve][Dual] 荣誉 UI patch 跳过：荣誉资源定义未找到");
            return;
        }

        // 荣誉变化 → 只刷新 NCombatUi 树上的荣誉行。
        SecondaryResourceHook.RegisterGlobalListener(new HonorUiRefreshListener(honorDef));

        // NCombatUi.Activate：初始绑定也按奥吉尔玩家刷新（主/副无关）。
        var activate = AccessTools.Method(NCombatUiType, "Activate", new[] { typeof(CombatState) });
        if (activate == null)
        {
            // 版本漂移兜底：按名+单 CombatState 参数扫描。
            foreach (var method in NCombatUiType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != "Activate") continue;
                var ps = method.GetParameters();
                if (ps.Length != 1 || ps[0].ParameterType != typeof(CombatState)) continue;
                activate = method;
                break;
            }
        }

        if (activate != null)
        {
            new Harmony("foreve.dual_character_honor_ui")
                .Patch(activate, postfix: new HarmonyMethod(GetMethod(nameof(ActivatePostfix))));
        }

        logger.Info($"[Foreve][Dual] 荣誉 UI patch 已装 (Activate={activate != null})");
    }

    [HarmonyPriority(Priority.Last)]
    private static void ActivatePostfix(NCombatUi __instance, CombatState state)
    {
        try
        {
            if (!DualCharacterState.Enabled || state?.Players == null || state.Players.Count < 2) return;
            // 直接从当前战斗状态找奥吉尔玩家，避免静态 MainPlayer/SecondaryPlayer 引用
            // 在个别开局/读档时序下未写入导致荣誉行漏刷新。
            var honorPlayer = state.Players.FirstOrDefault(p => p.Character is OgierCharacter);
            if (honorPlayer == null) return;
            RefreshHonorRows(__instance, honorPlayer);
            Godot.GD.Print("[Foreve][Dual][HonorUi] Activate 刷新荣誉行（按奥吉尔玩家）");
        }
        catch (Exception e)
        {
            Godot.GD.Print($"[Foreve][Dual] 荣誉 UI Activate 刷新异常: {e.Message}");
        }
    }

    private static void RefreshHonorRows(Node? root, Player honorPlayer)
    {
        if (root == null || !Godot.GodotObject.IsInstanceValid(root)) return;
        if (honorPlayer == null ||
            !ModSecondaryResourceRegistry.TryGet(OgierCharacter.HonorResourceId, out var honorDef))
            return;

        // 只刷新荣誉行自身；绝不调 SecondaryResourceUiRuntime.UpdateCombatUi(root, ...)，
        // 否则银钥行也会被刷新成奥吉尔玩家的银钥（银钥实际在主玩家），导致错误隐藏/重复。
        RefreshHonorRowsRecursive(root, honorPlayer, honorDef);
    }

    private static void RefreshHonorRowsRecursive(
        Node root,
        Player honorPlayer,
        SecondaryResourceDefinition honorDef)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is OgierHonorCounterRow row)
            {
                row.SetFollowedPlayer(honorPlayer);
                row.Refresh(honorPlayer, [honorDef]);
                continue;
            }

            RefreshHonorRowsRecursive(child, honorPlayer, honorDef);
        }
    }

    /// <summary>奥吉尔玩家荣誉变化后刷新荣誉行（主/副无关）。</summary>
    private sealed class HonorUiRefreshListener(SecondaryResourceDefinition honorDef)
        : ISecondaryResourceHookListener
    {
        public Task AfterSecondaryResourceChanged(SecondaryResourceChangeContext context)
        {
            try
            {
                if (!DualCharacterState.Enabled || context.Definition != honorDef) return Task.CompletedTask;
                if (context.Player?.Character is not OgierCharacter) return Task.CompletedTask;

                var ui = NCombatRoom.Instance?.Ui;
                if (ui == null) return Task.CompletedTask;
                RefreshHonorRows(ui, context.Player);
                Godot.GD.Print($"[Foreve][Dual][HonorUi] 荣誉变化刷新 {context.OldAmount} -> {context.NewAmount}");
            }
            catch (Exception e)
            {
                Godot.GD.Print($"[Foreve][Dual] 荣誉 UI 变化刷新异常: {e.Message}");
            }
            return Task.CompletedTask;
        }
    }

    private static MethodInfo GetMethod(string name)
        => typeof(DualCharacterHonorUiPatch).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
}
