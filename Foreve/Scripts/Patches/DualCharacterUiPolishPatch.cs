using System;
using System.Collections.Generic;
using System.Reflection;

using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：联机 UI 打磨 + 意图描述文本修正（批次 3 边角）。
///
/// 1. 主玩家 NMultiplayerPlayerState 面板隐藏（Postfix NMultiplayerPlayerStateContainer.Initialize）：
///    双人局里联机玩家状态条（名字/HP/手牌数）会为两名玩家各建一块面板（NGlobalUi.Initialize
///    → MultiplayerPlayerContainer.Initialize：先建本地玩家 = 主玩家，再为其余玩家各建一块）。
///    双人模式下把「本地玩家（主玩家）」的面板 Visible=false，保留副玩家的原生联机缩略条——
///    副玩家血量改由原生面板承载，顶栏不再另加副血条 Label（2026-08-15 用户拍板）。
///    判定用 LocalContext.GetMe(runState) 而非 DualCharacterState.MainPlayer 引用
///    （Initialize 与 RunStartedEvent 合并的先后顺序无关）。
///    原版/真联机多人不受影响（IsDualMode 为 false 时直接放行）。
///
/// 2. 副玩家 HP 常显（Postfix NMultiplayerPlayerState.UpdateHighlightedState）：
///    原版缩略条「悬停才展开显示血量」（UpdateHighlightedState → _healthBar.FadeInHpLabel/FadeOutHpLabel，
///    IL 189770 实证）。双人模式下副玩家面板每次状态刷新后强制 FadeInHpLabel(0.1)——
///    血量始终可见，无需悬停（2026-08-15 用户拍板）。主玩家面板已被隐藏不受影响；
///    原版/真联机多人不受影响（Enabled/副玩家引用判定）。

/// 3. 副玩家顶栏信息行精简（Postfix NMultiplayerPlayerState._Ready + OnCombatSetUp）：
///    删除副角色状态栏中的卡组数量与图表、能量数量与图表，以及两者左侧的数字 2。
///    CharacterIcon/HealthBar 不在 TopInfoContainer 内，只把 _topContainer.Visible=false，
///    头像与血条保持原状；主玩家/真联机多人不受影响。
///    若名字标签独立于该行且文本回退为纯数字 2，也会一并隐藏。


///
/// 4. 意图描述单目标化（Prefix AbstractIntent.GetIntentDescription，IL 1095335-1095365）：
///    原方法把 IsMultiplayer = (owner.CombatState.RunState.Players.Count &gt; 1) 注入本地化变量
///    （"intents" 表 {prefix}.description），双人局 Count==2 → 恒显示"对全体玩家造成 X"，
///    与批次 1b 的随机单目标攻击矛盾。双人模式下改为：
///       - 普通战/攻击招式（单目标语义）→ IsMultiplayer = false（显示"造成 X"）；
///       - 精英/Boss 战 debuff AOE（两名全中，DualCharacterTargeting.IsDebuffAoeMove）→ true
///         （"对全体玩家"描述正确，保持）。
///    AttackIntent/StatusIntent 覆写均 call base 本方法，一处 patch 全覆盖；真联机多人
///    （非双人模式）放行原逻辑。本地化键在游戏主 pck 不可改，故采用变量注入方案。
///
/// ⚠️ 接线（主流程在 Entry.cs 统一处理，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterUiPolishPatch.Install(Logger);
///   （与 Entry.cs 中 DualCharacterCombatPatches.Install(Logger) 等并列）
/// </summary>
public static class DualCharacterUiPolishPatch
{
    private static Logger _logger = null!;
    private static readonly MethodInfo IntentPrefixGetter =
        AccessTools.PropertyGetter(typeof(AbstractIntent), "IntentPrefix");

    /// <summary>NMultiplayerPlayerState 顶栏信息容器（卡组/能量计数与图标、数字 2 所在行）。</summary>
    private static readonly FieldInfo? TopInfoContainerField =
        AccessTools.Field(typeof(NMultiplayerPlayerState), "_topContainer");

    /// <summary>名字标签：双人假联机下副玩家名称可能回退为纯数字（如 2），用户要求一并去掉。</summary>
    private static readonly FieldInfo? NameplateLabelField =
        AccessTools.Field(typeof(NMultiplayerPlayerState), "_nameplateLabel");



    public static void Install(Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.dual_character_ui_polish");

        var containerInit = AccessTools.DeclaredMethod(typeof(NMultiplayerPlayerStateContainer), "Initialize");
        if (containerInit == null)
        {
            _logger.Warn("[Foreve][Dual] NMultiplayerPlayerStateContainer.Initialize NOT FOUND - skip secondary panel hide");
        }
        else
        {
            harmony.Patch(containerInit, postfix: new HarmonyMethod(
                typeof(DualCharacterUiPolishPatch).GetMethod(nameof(ContainerInitializePostfix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
            _logger.Info("[Foreve][Dual] secondary NMultiplayerPlayerState panel hide installed");
        }

        var intentDesc = AccessTools.DeclaredMethod(typeof(AbstractIntent), "GetIntentDescription");
        if (intentDesc == null)
        {
            _logger.Warn("[Foreve][Dual] AbstractIntent.GetIntentDescription NOT FOUND - skip intent text fix");
        }
        else
        {
            harmony.Patch(intentDesc, prefix: new HarmonyMethod(
                typeof(DualCharacterUiPolishPatch).GetMethod(nameof(GetIntentDescriptionPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
            _logger.Info("[Foreve][Dual] intent description IsMultiplayer fix installed");
        }

        // 副角色顶栏精简：隐藏 TopInfoContainer（卡组/能量计数与图标、数字 2），保留头像与血条。
        var ready = AccessTools.Method(typeof(NMultiplayerPlayerState), "_Ready");
        if (ready == null)
        {
            _logger.Warn("[Foreve][Dual] NMultiplayerPlayerState._Ready NOT FOUND - skip secondary top info hide on ready");
        }
        else
        {
            harmony.Patch(ready, postfix: new HarmonyMethod(
                typeof(DualCharacterUiPolishPatch).GetMethod(nameof(SecondaryPanelReadyPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
        }

        // OnCombatSetUp 会把卡组/能量容器重新 Visible=true；在其后再次隐藏 TopInfoContainer。
        var combatSetUp = AccessTools.Method(typeof(NMultiplayerPlayerState), "OnCombatSetUp",
            new[] { typeof(CombatState) });
        if (combatSetUp == null)
        {
            _logger.Warn("[Foreve][Dual] NMultiplayerPlayerState.OnCombatSetUp NOT FOUND - skip secondary top info hide on combat setup");
        }
        else
        {
            harmony.Patch(combatSetUp, postfix: new HarmonyMethod(
                typeof(DualCharacterUiPolishPatch).GetMethod(nameof(SecondaryPanelCombatSetUpPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
            _logger.Info("[Foreve][Dual] secondary multiplayer state top info hide installed");
        }


        var updateHighlighted = AccessTools.Method(typeof(NMultiplayerPlayerState), "UpdateHighlightedState");
        if (updateHighlighted == null)
        {
            _logger.Warn("[Foreve][Dual] NMultiplayerPlayerState.UpdateHighlightedState NOT FOUND - skip secondary hp always shown");
        }
        else
        {
            harmony.Patch(updateHighlighted, postfix: new HarmonyMethod(
                typeof(DualCharacterUiPolishPatch).GetMethod(nameof(UpdateHighlightedStatePostfix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
            _logger.Info("[Foreve][Dual] secondary multiplayer state hp always-shown installed");
        }
    }

    /// <summary>
    /// 双人模式：副玩家面板血量常显。原版 UpdateHighlightedState 在悬停结束时
    /// FadeOutHpLabel(0.5, 0) 收起血量——本 Postfix 紧随其后强制 FadeInHpLabel(0.1) 再淡入。
    /// 所有触发源（悬停进出/焦点/战斗数值刷新）都会经过 UpdateHighlightedState，单点覆盖。
    /// </summary>
    private static void UpdateHighlightedStatePostfix(NMultiplayerPlayerState __instance)
    {
        if (!DualCharacterState.Enabled) return;
        var secondary = DualCharacterState.SecondaryPlayer;
        if (secondary == null || !ReferenceEquals(__instance.Player, secondary)) return;
        HideSecondaryTopInfo(__instance);


        try
        {
            var healthBar = AccessTools.Field(typeof(NMultiplayerPlayerState), "_healthBar")?.GetValue(__instance);
            if (healthBar == null) return;
            AccessTools.Method(healthBar.GetType(), "FadeInHpLabel")?.Invoke(healthBar, new object[] { 0.1f });
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] secondary hp always-shown failed: {e.Message}");
        }
    }

    /// <summary>双人模式：隐藏主玩家（本地玩家）的 NMultiplayerPlayerState 面板，保留副玩家的原生缩略条。</summary>
    private static void ContainerInitializePostfix(NMultiplayerPlayerStateContainer __instance, RunState runState)
    {
        if (!DualCharacterState.IsDualMode(runState)) return;

        Player? me = null;
        try { me = LocalContext.GetMe(runState); }
        catch { /* 获取本地玩家失败则跳过（保守） */ }
        if (me == null) return;

        var count = __instance.GetChildCount(false);
        for (var i = 0; i < count; i++)
        {
            if (__instance.GetChild(i, false) is NMultiplayerPlayerState state
                && state.Player != null
                )
            {
                if (ReferenceEquals(state.Player, me))
                {
                    state.Visible = false;
                }

                if (DualCharacterState.IsSecondaryPlayer(state.Player))
                {
                    HideSecondaryTopInfo(state); // 副玩家：隐藏顶栏信息行
                }
            }
            // 副玩家顶栏精简已在主/副分支内处理。

        }
    }

    /// <summary>NMultiplayerPlayerState._Ready 后：副玩家面板隐藏 TopInfoContainer（卡组/能量计数与图标、数字 2）。</summary>
    private static void SecondaryPanelReadyPostfix(NMultiplayerPlayerState __instance)
    {
        if (IsSecondaryPanel(__instance))
            HideSecondaryTopInfo(__instance);
    }

    /// <summary>OnCombatSetUp 会把卡组/能量容器重新设为可见；本 Postfix 在其后再次精简副玩家顶栏。</summary>
    private static void SecondaryPanelCombatSetUpPostfix(NMultiplayerPlayerState __instance)
    {
        if (IsSecondaryPanel(__instance))
            HideSecondaryTopInfo(__instance);
    }

    private static bool IsSecondaryPanel(NMultiplayerPlayerState? state)
    {
        if (!DualCharacterState.Enabled || state == null || state.Player == null) return false;
        return DualCharacterState.IsSecondaryPlayer(state.Player);
    }

    /// <summary>
    /// 只隐藏 _topContainer（TopInfoContainer）：卡组数量/图表、能量数量/图表与数字 2 都在这行里；
      /// 若名字标签独立于该行且文本回退为纯数字 2，也一并隐藏；
    /// CharacterIcon 与 HealthBar 是容器外独立节点，不在此行，因此头像和血条保持不变。
    /// </summary>
    private static void HideSecondaryTopInfo(NMultiplayerPlayerState state)
    {
        try
        {
            if (TopInfoContainerField?.GetValue(state) is Godot.CanvasItem topInfoContainer)
                topInfoContainer.Visible = false;

            // 双人假联机的副玩家名字可能回退为 "2"；若名字标签独立于 TopInfoContainer，
            // 且当前文本就是纯数字 2，则一并隐藏（头像/血条不受影响）。
            if (NameplateLabelField?.GetValue(state) is Godot.CanvasItem nameplate)
            {
                var text = nameplate.GetType().GetProperty("Text")?.GetValue(nameplate) as string;
                if (text != null && text.Trim() == "2")
                    nameplate.Visible = false;
            }

        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] secondary top info hide failed: {e.Message}");
        }
    }


    /// <summary>
    /// 双人模式重写 IsMultiplayer 本地化变量：单目标攻击 → false；精英/Boss debuff AOE → true。
    /// 其余情况（单玩家/真联机多人）返回 true 放行原方法，行为零变化。
    /// </summary>
    private static bool GetIntentDescriptionPrefix(AbstractIntent __instance, Creature owner, ref LocString __result)
    {
        var combat = owner?.CombatState;
        if (!DualCharacterState.IsDualMode(combat)) return true;

        // 精英/Boss 战 debuff AOE：两名全中，"对全体玩家"描述正确，保持 true；
        // 其余（普通战攻击/防御等单目标招式）：单目标语义，IsMultiplayer = false
        bool isMultiplayer = false;
        try
        {
            isMultiplayer = DualCharacterTargeting.IsDebuffAoeMove(owner!.Monster, combat!);
        }
        catch
        {
            isMultiplayer = false; // 判定异常按单目标处理（最保守）
        }

        var prefix = IntentPrefixGetter?.Invoke(__instance, null) as string ?? string.Empty;
        var loc = new LocString("intents", prefix + ".description");
        loc.Add("IsMultiplayer", isMultiplayer);
        __result = loc;
        return false;
    }
}
