using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace Foreve.Scripts.Patches;

/// <summary>事件进入崩溃兜底：EventOption.AddLocVars 构造选项本地化变量时，
/// 原版 IL 只对 eventModel.Owner 判空（IL_0007 brtrue），随后直接
/// Owner.get_Character() → CharacterModel.AddDetailsTo(description)（IL_000c/IL_0017），
/// Owner != null 但 Owner.Character == null 时 callvirt 抛 NullReferenceException，
/// 事件进入界面直接报「你遇见了一个Bug」。
/// 兜底：prefix 检测 Character 为 null 时跳过原方法（副作用仅失去角色详情文本
/// 与多人在线标记，事件文本本身正常）。</summary>
public static class EventOptionAddLocVarsFix
{
    private static Logger Logger = null!;

    public static void Install(Logger logger)
    {
        Logger = logger;
        var harmony = new Harmony("foreve.event_option_add_loc_vars_fix");

        var method = typeof(EventOption).GetMethod("AddLocVars", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            Logger.Warn("[Foreve][Fix] EventOption.AddLocVars NOT FOUND - skip null-guard");
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(
            typeof(EventOptionAddLocVarsFix).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
        Logger.Info("[Foreve][Fix] EventOption.AddLocVars null-guard installed");
    }

    private static bool Prefix(EventModel eventModel)
    {
        try
        {
            // Owner == null 时原方法自带判空可正常走；仅 Owner.Character == null 会 NRE
            if (eventModel.Owner != null && eventModel.Owner.Character == null)
            {
                Logger.Warn("[Foreve][Fix] EventOption.AddLocVars skipped: Owner.Character is null");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[Foreve][Fix] AddLocVars prefix error: {ex.GetType().Name}: {ex.Message}");
        }
        return true;
    }
}
