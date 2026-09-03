using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.RunData;
using Foreve.Scripts.Data;

namespace Foreve.Scripts.SilverKey;

/// <summary>
/// 银钥资源（全局机制）：每消耗 1 点能量获得 1 点银钥（上限 5），
/// 跨战斗保留（Run 级持久化），每层打完（Boss 战胜利）后清零。
/// </summary>
public static class SilverKeyResource
{
    private const string SilverKeyLocalId = "silver_key";

    /// <summary>银钥资源的完整注册 ID（跨卡牌/能力/遗物统一使用）。</summary>
    public static string ResourceId => ModSecondaryResourceRegistry.GetResourceId(Foreve.Scripts.ForeveMod.ModId, SilverKeyLocalId);

    private static RunSavedData<SilverKeyRunData>? _savedData;

    /// <summary>银钥跨战斗存档（与 RitsuLib 的 Run 持久化互为双保险，规避非正常结束战斗不落盘的问题）。</summary>
    public sealed class SilverKeyRunData
    {
        public int Value;
    }

    public static void RegisterSilverKey()
    {
        var def = new SecondaryResourceDefinition(
            defaultAmount: 0,
            baseMaxAmount: 5,
            minAmount: 0,
            hardMaxAmount: 999,
            turnStartPolicy: SecondaryResourceTurnStartPolicy.None,
            persistencePolicy: SecondaryResourcePersistencePolicy.Run,
            locTable: SecondaryResourceDefinition.DefaultLocTable,
            titleKey: "FOREVE_RESOURCE_SILVER_KEY.title",
            descriptionKey: "FOREVE_RESOURCE_SILVER_KEY.description",
            // 银钥图标已生成（圆形银钥能量，2026-08-12）
            smallIconPath: "res://Foreve/Assets/UI/UI/silver_key_small.png",
            largeIconPath: "res://Foreve/Assets/UI/UI/silver_key_large.png");

        var boundDef = ModSecondaryResourceRegistry.For("foreve").Register(SilverKeyLocalId, def);
        // 全局显示（不限定角色）：任何角色战斗中都显示银钥
        ModSecondaryResourceRegistry.For("foreve").AlwaysShowInCombatUi(SilverKeyLocalId);
        ModSecondaryResourceRegistry.For("foreve").RegisterCombatUi(
            "silver_key_combat_ui",
            parent => new SilverKeyCounterRow(),
            update: ctx =>
            {
                // UI 激活时兜底恢复（CombatStartingEvent 时机可能早于 PlayerCombatState 创建，
                // Set 静默失败；NCombatUi 激活时战斗状态必已就绪——这里再补一次恢复）
                try
                {
                    if (_savedData != null && ctx.Player.RunState is RunState rs &&
                        _savedData.TryGet(rs, out var data) && data.Value > 0)
                    {
                        var current = SecondaryResourceCmd.Get(ctx.Player, boundDef.Id);
                        if (current <= 0)
                        {
                            GD.Print($"[Foreve] SilverKey UI激活恢复: 存档 {data.Value} -> Set (当前 {current})");
                            SecondaryResourceCmd.Set(ctx.Player, boundDef.Id, data.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    GD.Print($"[Foreve] SilverKey UI激活恢复异常: {ex.Message}");
                }
                ctx.Node.SetFollowedPlayer(ctx.Player);
                ctx.Node.Refresh(ctx.Player, ctx.VisibleDefinitions);
            });

        // 上限 5 钳制（SecondaryResourceCmd.Gain 只钳到 int.MaxValue，不会自动钳到 baseMax）
        SecondaryResourceHook.RegisterGlobalListener(new SilverKeyCapListener(boundDef));

        // 跨战斗存档：RunSavedData（键 ForeveDataKeys.SilverKeyAmount）。
        // RitsuLib 的 Run 持久化只在 AfterCombatEnd 保存且恢复时序依赖 PlayerCombatState，
        // 非正常结束战斗会漏存 → 下一场开局 0。这里在战斗结束/开始各做一次显式同步兜底。
        var store = RunSavedDataStore.For("foreve");
        _savedData = store.Register<SilverKeyRunData>(ForeveDataKeys.SilverKeyAmount, () => new SilverKeyRunData());

        // 每消耗 1 点能量 → 1 点银钥（打牌耗能）
        RitsuLibFramework.SubscribeLifecycle<EnergySpentEvent>(async e =>
        {
            try
            {
                if (e.Amount <= 0 || e.Card.Owner == null) return;
                // await 等待入账完成，避免 fire-and-forget 在战斗状态初始化竞争时静默丢量
                await SecondaryResourceCmd.Gain(e.Card.Owner, boundDef.Id, e.Amount);
            }
            catch (Exception ex)
            {
                GD.Print($"[Foreve] Silver key gain failed: {ex.Message}");
            }
        }, replayCurrentState: false);

        // 每层 Boss 战胜利后清零（一层打完）
        RitsuLibFramework.SubscribeLifecycle<CombatVictoryEvent>(e =>
        {
            if (e.Room.RoomType != RoomType.Boss) return;
            foreach (var player in e.RunState.Players)
            {
                SecondaryResourceCmd.Reset(player, boundDef.Id);
                Persist(player, boundDef);
            }
        }, replayCurrentState: false);

        // 战斗结束：把当前银钥值写入 Run 存档（双保险，覆盖非正常结束战斗的漏存）
        RitsuLibFramework.SubscribeLifecycle<CombatEndedEvent>(e =>
        {
            // 钥令三选一若仍打开则强制关闭，避免跨战斗残留
            SilverKeyOrderManager.OnCombatEnded();

            if (e.RunState is not RunState rs) return;
            foreach (var player in rs.Players)
            {
                Persist(player, boundDef);
                GD.Print($"[Foreve] SilverKey 战斗结束存档 -> {SecondaryResourceCmd.Get(player, boundDef.Id)}");
            }
        }, replayCurrentState: false);

        // 战斗开始：从 Run 存档恢复银钥值（覆盖 RitsuLib 恢复时序脆弱导致的第二场开局 0）
        RitsuLibFramework.SubscribeLifecycle<CombatStartingEvent>(e =>
        {
            // 每场战斗开始清空钥令抽取记录（本场已抽出的钥令不再重复）
            SilverKeyOrderManager.ResetForCombat();

            if (e.RunState is not RunState rs) return;
            foreach (var player in rs.Players)
            {
                if (_savedData == null || !_savedData.TryGet(rs, out var data)) continue;
                var current = SecondaryResourceCmd.Get(player, boundDef.Id);
                if (data.Value > 0 && current <= 0)
                {
                    GD.Print($"[Foreve] SilverKey 战斗开始恢复: 存档 {data.Value} -> Set");
                    SecondaryResourceCmd.Set(player, boundDef.Id, data.Value);
                }
            }
        }, replayCurrentState: false);

    }

    private static void Persist(Player player, SecondaryResourceDefinition boundDef)
    {
        if (_savedData == null) return;
        if (player.RunState is not RunState rs) return;
        var amount = SecondaryResourceCmd.Get(player, boundDef.Id);
        _savedData.Modify(rs, d => d.Value = amount);
    }

    /// <summary>把银钥获得量钳制到剩余容量内（上限 5）。</summary>
    private sealed class SilverKeyCapListener(SecondaryResourceDefinition silverKeyDef)
        : ISecondaryResourceHookListener
    {
        public decimal ModifySecondaryResourceGain(SecondaryResourceContext context, decimal amount)
        {
            if (context.Definition != silverKeyDef) return amount;

            var current = SecondaryResourceCmd.Get(context.Player, silverKeyDef.Id);
            var max = SecondaryResourceCmd.GetMax(context.Player, silverKeyDef.Id);
            if (max == null) return amount;

            var available = max.Value - current;
            if (available <= 0) return 0;
            return Math.Min(amount, available);
        }
    }
}
