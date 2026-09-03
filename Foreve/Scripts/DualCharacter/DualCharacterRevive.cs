using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;

namespace Foreve.Scripts.DualCharacter;

/// <summary>
/// 双角色模式：复活管理（批次 2a）。全部 IL 结论来自 C:\tmp\sts2_full.il（2026-08-14 实证）。
///
/// 功能：
///   1. 公开复活 API：ReviveCharacter(Player, percent) —— 战斗内/战斗外通用（percent 为 0~100，
///      100=满血），幂等（活人不处理），自动恢复玩家 hooks（HealInternal 内置）。
///   2. 篝火复活（批次 2c 改版）：不再进房自动复活 —— 改为双人模式下把原版篝火
///      「给队友回血」选项替换为「复活」选项（点击复活，百分比=RestSiteRevivePercent(进阶)，
///      无人死亡时按钮禁用），见 Scripts/Patches/DualCharacterRestSiteRevivePatch.cs。
///   3. 新的一层开始时全员满血复活（100%）。
///
/// ── IL 调研结论（本批次实证，写码依据） ──────────────────────────────────────
///   1. 战斗外复活可行性：CreatureCmd.Heal(creature, decimal, bool) 定义于 IL 1826694，
///      方法体（MoveNext IL 1830919~1831323）只依赖 CombatManager.Instance（Godot 常驻单例）
///      的 IsEnding/IsInProgress 做分支，**不要求处于战斗**；原版篝火回血
///      HealRestSiteOption.&lt;ExecuteRestSiteHeal&gt;d__13.MoveNext（IL 1746976~1747138）
///      就是在战斗外直接调 CreatureCmd.Heal(creature, GetHealAmount(player), true) ——
///      **结论：战斗外直接可用 Heal，无需反射改 _currentHp 字段**。
///   2. hooks 恢复：Creature.HealInternal（IL 1766488~1766525）在「死亡→复活」时自动调用
///      player.ActivateHooks()（IL 1766519-1766520 区域）并 Invoke Revived 事件 ——
///      **结论：复活 API 无需手动恢复 hooks，HealInternal 已处理**。
///   3. 进阶取值：IRunState.get_AscensionLevel()（IL 156477，public）→ runState.AscensionLevel
///      （RunState 具体类 IL 163401 调用点实证）。
///   4. 篝火机制（批次 2c 更新）：RestSiteRoom.get_Options（IL 197999）→ RestSiteSynchronizer
///      GetLocalOptions（IL 1077727）；选项由 RestSiteOption.Generate(Player)（IL 1748648）构建：
///      [HEAL, SMITH] + 玩家数>1 时追加 MendRestSiteOption(MEND=给队友回血, IL 1747663)，列表存入
///      _restSites[i].options，UI 与 ChooseOption 执行读同一列表。**本批次方案：patch Generate
///      Postfix 把 MEND 换成 mod 复活选项（ForeveRestSiteReviveOption），点击复活死亡角色，
///      百分比=RestSiteRevivePercent(进阶)；IsEnabled=false 时原版按钮置灰不可点（NRestSiteButton.Create
///      IL 782552 用 !IsEnabled 置 _isUnclickable）——详见 DualCharacterRestSiteRevivePatch.cs**。
///   5. 新层事件：RitsuLib RoomLifecycleContracts.cs 提供 ActEnteredEvent(IRunState RunState,
///      int CurrentActIndex, DateTimeOffset OccurredAtUtc) —— 订阅它做满血复活。
///
/// ⚠️ 接线（主流程统一在 Entry.cs 处理，不要直接改 Entry.cs）：
///   Foreve.Scripts.DualCharacter.DualCharacterRevive.Install(Logger);
/// </summary>
public static class DualCharacterRevive
{
    private static MegaCrit.Sts2.Core.Logging.Logger? _logger;

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        _logger = logger;

        // 新的一层开始 → 全员满血复活（篝火复活已移交 DualCharacterRestSiteRevivePatch 的选项替换，
        // 批次 2c 起不再进房自动复活）
        RitsuLibFramework.SubscribeLifecycle<ActEnteredEvent>(OnActEntered, replayCurrentState: false);

        logger.Info("[Foreve][Dual] 复活管理已安装 (ReviveCharacter API / 新层满血复活；篝火复活见 RestSiteRevivePatch)");
    }

    /// <summary>
    /// 篝火复活百分比：100 − 5×进阶，钳制到 [50, 100]。
    /// A0=100%、A1=95%、…、A10 及以上=50%。
    /// </summary>
    public static decimal RestSiteRevivePercent(int ascensionLevel)
        => Math.Clamp(100m - 5m * ascensionLevel, 50m, 100m);

    /// <summary>
    /// 公开复活 API：把 player 的角色复活到 MaxHp×percent/100 血量。
    /// percent 取值 [0,100]（100=满血），内部钳制；幂等：角色未死亡直接返回。
    /// 战斗内/战斗外通用（CreatureCmd.Heal 战斗外可用，见类头 IL 结论 1）；
    /// hooks 恢复由 HealInternal 自动完成（见类头 IL 结论 2）。
    /// </summary>
    public static async Task ReviveCharacter(Player player, decimal percent)
    {
        if (player?.Creature == null) return;
        var creature = player.Creature;
        if (!creature.IsDead) return; // 幂等：活人不处理

        var clamped = Math.Clamp(percent, 0m, 100m);
        var amount = Math.Floor(creature.MaxHp * clamped / 100m);
        if (amount < 1m) amount = 1m; // 至少 1 点（0 点治疗不会触发复活）

        await CreatureCmd.Heal(creature, amount, true);
        GD.Print($"[Foreve][Dual] 复活 {GetCharacterName(player)} 至 {amount}/{creature.MaxHp} HP ({clamped}%)");
    }

    // ── 事件入口 ──────────────────────────────────────────────────────────

    private static void OnActEntered(ActEnteredEvent e)
    {
        // 新的一层：满血复活（双人模式）
        if (DualCharacterState.IsDualMode(e.RunState as RunState))
        {
            _ = ReviveAllDeadAsync(e.RunState, 100m, "新章节");
        }
    }

    private static async Task ReviveAllDeadAsync(IRunState runState, decimal percent, string reason)
    {
        try
        {
            foreach (var p in runState.Players)
            {
                if (p?.Creature != null && p.Creature.IsDead)
                {
                    await ReviveCharacter(p, percent);
                }
            }
            _logger?.Info($"[Foreve][Dual] {reason}复活检查完成 (percent={percent}%)");
        }
        catch (Exception ex)
        {
            _logger?.Warn($"[Foreve][Dual] {reason}复活失败: {ex}");
        }
    }

    private static string GetCharacterName(Player player)
    {
        try
        {
            var text = player.Character?.Title?.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 角色名获取异常: {e}");
        }
        return player.NetId.ToString();
    }
}
