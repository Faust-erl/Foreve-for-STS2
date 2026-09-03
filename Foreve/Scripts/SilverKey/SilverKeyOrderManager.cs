using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using STS2RitsuLib.Combat.SecondaryResources;

namespace Foreve.Scripts.SilverKey;

/// <summary>
/// 银钥按钮的抽取/执行流程：
///   1. 银钥 ≥5 时按钮显著变亮并可点击；
///   2. 点击后一次性消耗当前全部银钥（5→0）；
///   3. 从所有钥令中随机抽取 3 个可选项（同一场战斗内已抽出的钥令不会再次出现）；
///   4. 玩家三选一后执行对应钥令效果。
/// </summary>
public static class SilverKeyOrderManager
{
    private static readonly object Gate = new();

    /// <summary>本场战斗已抽取过的钥令 Code（每场战斗开始清空）。</summary>
    private static readonly HashSet<string> DrawnCodes = new();

    private static bool _selectionOpen;

    public static bool IsSelectionOpen
    {
        get { lock (Gate) return _selectionOpen; }
        private set { lock (Gate) _selectionOpen = value; }
    }

    /// <summary>战斗开始时清空本场钥令抽取记录（由 SilverKeyResource 的 CombatStartingEvent 调用）。</summary>
    public static void ResetForCombat()
    {
        lock (Gate)
        {
            DrawnCodes.Clear();
            _selectionOpen = false;
        }
    }

    /// <summary>战斗结束等外部中断时关闭正在展示的三选一弹窗（避免跨战斗残留）。</summary>
    public static void OnCombatEnded()
    {
        lock (Gate) _selectionOpen = false;
        SilverKeyOrderUi.ForceClose();
    }

    /// <summary>银钥按钮点击入口。返回是否成功触发一次钥令抽取。</summary>
    public static async Task<bool> TryInvokeAsync(Player? player)
    {
        if (player?.Creature?.CombatState == null) return false;

        lock (Gate)
        {
            if (_selectionOpen) return false;
            _selectionOpen = true;
        }

        try
        {
            var current = SecondaryResourceCmd.Get(player, SilverKeyResource.ResourceId);
            if (current < 5)
            {
                GD.Print($"[Foreve][SilverKey] 银钥不足 5（当前 {current}），不抽取钥令");
                return false;
            }

            var options = RollOptions();
            if (options.Count == 0)
            {
                GD.Print("[Foreve][SilverKey] 本场已抽完所有钥令，不消耗银钥");
                return false;
            }

            // 点击即消耗当前全部银钥（5→0）；若后续 UI 异常，由 UI 回退执行第一项兜底
            var spent = await SecondaryResourceCmd.Spend(
                player, SilverKeyResource.ResourceId, current, null, null);
            if (!spent)
            {
                GD.Print("[Foreve][SilverKey] 银钥消耗失败，取消钥令抽取");
                return false;
            }

            var chosen = await SilverKeyOrderUi.ShowAsync(player, options);
            if (chosen == null)
            {
                GD.Print("[Foreve][SilverKey] 钥令选择被关闭，不执行");
                return false;
            }

            GD.Print($"[Foreve][SilverKey] 执行钥令 {chosen.Code}-{chosen.Name}");
            await SilverKeyOrderEffects.ExecuteAsync(chosen, player);
            return true;
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][SilverKey] 钥令流程异常: {ex}");
            return false;
        }
        finally
        {
            IsSelectionOpen = false;
        }
    }

    /// <summary>从尚未抽出的钥令中随机取 3 个；抽取后立即记入本场已抽集合（同一场不重复）。</summary>
    private static IReadOnlyList<SilverKeyOrderDefinition> RollOptions()
    {
        List<SilverKeyOrderDefinition> pool;
        lock (Gate)
        {
            pool = SilverKeyOrderCatalog.All.Where(o => !DrawnCodes.Contains(o.Code)).ToList();
        }

        if (pool.Count == 0) return Array.Empty<SilverKeyOrderDefinition>();

        for (var i = pool.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var picked = pool.Take(3).ToList();
        lock (Gate)
        {
            foreach (var order in picked)
                DrawnCodes.Add(order.Code);
        }

        GD.Print($"[Foreve][SilverKey] 钥令三选一: {string.Join(", ", picked.Select(o => o.Code))} "
            + $"(本场已抽 {DrawnCodes.Count}/{SilverKeyOrderCatalog.All.Count})");
        return picked;
    }
}
