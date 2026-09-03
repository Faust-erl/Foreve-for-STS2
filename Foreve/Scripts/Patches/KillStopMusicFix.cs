using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;

namespace Foreve.Scripts.Patches;

/// <summary>游戏原版 bug 修复：放弃/死亡流程中 Kill 状态机的 GameOver 分支执行
/// NRun.Instance.RunMusicController.StopMusic()，而放弃时 RunMusicController 为 null，
/// callvirt 直接抛 NRE 中断 Abandon 流程（游戏卡死）。将调用替换为 null 安全包装。</summary>
public static class KillStopMusicFix
{
    private static Logger Logger = null!;

    public static void Install(Logger logger)
    {
        Logger = logger;
        var harmony = new Harmony("foreve.kill_stop_music_fix");

        var d14 = typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd).GetNestedType("<Kill>d__14", BindingFlags.NonPublic);
        if (d14 == null) { Logger.Warn("[Foreve][Fix] <Kill>d__14 NOT FOUND - skip StopMusic fix"); return; }
        var moveNext = d14.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
        if (moveNext == null) { Logger.Warn("[Foreve][Fix] Kill MoveNext NOT FOUND - skip StopMusic fix"); return; }

        harmony.Patch(moveNext, transpiler: new HarmonyMethod(
            typeof(KillStopMusicFix).GetMethod(nameof(Transpiler), BindingFlags.Static | BindingFlags.NonPublic)));
        Logger.Info("[Foreve][Fix] Kill StopMusic null-guard installed");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instrs)
    {
        var list = new List<CodeInstruction>(instrs);
        var safeStopMusic = AccessTools.Method(typeof(KillStopMusicFix), nameof(SafeStopMusic));

        var patched = false;
        for (var i = 0; i < list.Count; i++)
        {
            var ci = list[i];
            if (ci.opcode != OpCodes.Callvirt || ci.operand is not MethodInfo mi) continue;
            if (mi.Name != "StopMusic") continue;

            var j = i - 1;
            while (j >= 0 && !(list[j].opcode == OpCodes.Callvirt && (list[j].operand as MethodInfo)?.Name == "get_RunMusicController")) j--;
            if (j < 0) continue;

            list.RemoveRange(j, i - j + 1);
            list.Insert(j, new CodeInstruction(OpCodes.Call, safeStopMusic));
            patched = true;
            break;
        }

        if (patched) Logger.Info("[Foreve][Fix] StopMusic call replaced with null-guard");
        else Logger.Warn("[Foreve][Fix] StopMusic call NOT found - game layout changed?");
        return list;
    }

    private static void SafeStopMusic(NRun nrun)
    {
        try
        {
            var mc = nrun?.RunMusicController;
            if (mc == null) return;
            mc.StopMusic();
        }
        catch (Exception ex) { Logger.Error($"[Foreve][Fix] SafeStopMusic error: {ex.GetType().Name}: {ex.Message}"); }
    }
}
