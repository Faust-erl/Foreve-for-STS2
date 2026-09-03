using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using STS2RitsuLib.Combat.SecondaryResources;
using STS2RitsuLib.Interop.AutoRegistration;
using STS2RitsuLib.RunData;
using STS2RitsuLib.Scaffolding.Characters;
using STS2RitsuLib.Scaffolding.Godot;
using Foreve.Scripts.Data;
using Foreve.Scripts.DualCharacter;
using Foreve.Scripts.Enums;
using Foreve.Scripts.Interfaces;

namespace Foreve.Scripts.Characters.Ogier;

[RegisterCharacter]
public class OgierCharacter : ModCharacterTemplate<OgierCardPool, OgierRelicPool, OgierPotionPool>,
    IForeveCharacter
{
    public const string HonorResourceId = "FOREVE_SECONDARY_RESOURCE_HONOR";
    private static RunSavedData<MaxHonorTracker>? _maxHonorData;

    public sealed class MaxHonorTracker
    {
        public int Value;
    }

    public ForeveCharacterArchetype Archetype => ForeveCharacterArchetype.Defense;
    public int UnlockPriority => 0;
    public string UnlockConditionDescription => "初始角色";

    public override Color NameColor => new(0.85f, 0.7f, 0.2f);
    public override Color EnergyLabelOutlineColor => new(0.7f, 0.55f, 0.1f);
    public override Color MapDrawingColor => new(0.85f, 0.7f, 0.2f);

    public override CharacterGender Gender => CharacterGender.Masculine;

    public override int StartingHp => 37;
    public override int StartingGold => 59;

    public override CharacterAssetProfile AssetProfile => CharacterAssetProfiles.Merge(
        CharacterAssetProfiles.Ironclad(),
        new(
            Scenes: new(
                VisualsPath: "res://Foreve/Assets/Characters/Ogier/ogier_character.tscn",
                EnergyCounterPath: "res://Foreve/Assets/Characters/Ogier/ogier_energy_counter.tscn",
                MerchantAnimPath: "res://Foreve/Assets/Characters/Ogier/ogier_merchant.tscn"
            ),
            Ui: new(
                IconTexturePath: "res://Foreve/Assets/Characters/Ogier/Ogier/images/ogier_portrait.png",
                IconPath: "res://Foreve/Assets/Characters/Ogier/ogier_icon.tscn",
                CharacterSelectBgPath: "res://Foreve/Assets/Characters/Ogier/ogier_bg.tscn",
                CharacterSelectIconPath: "res://Foreve/Assets/Characters/Ogier/Ogier/images/char_select_ogier.png",
                CharacterSelectLockedIconPath: "res://Foreve/Assets/Characters/Ogier/Ogier/images/char_select_ogier_locked.png",
                MapMarkerPath: "res://Foreve/Assets/Characters/Ogier/Ogier/images/ogier_portrait.png"
            )
        ));

    public override float AttackAnimDelay => 0f;
    public override float CastAnimDelay => 0f;

    public override bool RequiresEpochAndTimeline => false;

    public override List<string> GetArchitectAttackVfx() => new();

    protected override NCreatureVisuals? TryCreateCreatureVisuals()
    {
        // 场景资源缺失时返回 null，让游戏 fallback 基础角色立绘（避免战斗黑屏）
        var path = AssetProfile.Scenes?.VisualsPath;
        if (string.IsNullOrWhiteSpace(path) || !ResourceLoader.Exists(path))
            return null;
        return RitsuGodotNodeFactories.CreateFromScenePath<NCreatureVisuals>(path);
    }

    internal static void RegisterHonor()
    {
        var honorDef = new SecondaryResourceDefinition(
            defaultAmount: 0,
            baseMaxAmount: 5,
            minAmount: 0,
            hardMaxAmount: 999,
            turnStartPolicy: SecondaryResourceTurnStartPolicy.None,
            persistencePolicy: SecondaryResourcePersistencePolicy.Combat,
            locTable: SecondaryResourceDefinition.DefaultLocTable,
            titleKey: "FOREVE_RESOURCE_HONOR.title",
            descriptionKey: "FOREVE_RESOURCE_HONOR.description",
            smallIconPath: "res://Foreve/Assets/UI/UI/honor_icon_small.png",
            largeIconPath: "res://Foreve/Assets/UI/UI/honor_icon_large.png"
        );
        var boundDef = ModSecondaryResourceRegistry.For("foreve").Register("honor", honorDef);
        ModSecondaryResourceRegistry.For("foreve").AlwaysShowInCombatUiForCharacter<OgierCharacter>("honor");
        ModSecondaryResourceRegistry.For("foreve").RegisterCombatUi(
            "honor_combat_ui",
            parent => new OgierHonorCounterRow(),
            update: ctx =>
            {
                // 双人模式：RitsuLib 只对主玩家刷新战斗 UI（GetMe 恒返回主玩家）——荣誉行必须
                // 跟随奥吉尔玩家。否则银钥等主玩家资源每次变化都会把荣誉行重新绑回主玩家
                // （主玩家荣誉为 0 → 行隐藏），表现为「荣誉标识不显示」（2026-08-26 实测）。
                var player = ResolveHonorDisplayPlayer(ctx.Player);
                ctx.Node.SetFollowedPlayer(player);
                // 荣誉行只渲染荣誉本身：基类 Refresh 会把可见定义里每个资源都渲染成
                // 「图标+数字」计数器 → 血条下方出现重复银钥计数（银钥由 SilverKeyCounterRow 独占）。
                // 可见性以「实际显示玩家」判定（不能用 ctx.VisibleDefinitions：奥吉尔为副角色时
                // 该列表按主玩家计算，不含荣誉）。
                var honorDef = ModSecondaryResourceRegistry.TryGet(HonorResourceId, out var def) ? def : null;
                var honorOnly = player?.Character is OgierCharacter && honorDef != null
                    ? new[] { honorDef }
                    : Array.Empty<SecondaryResourceDefinition>();
                ctx.Node.Refresh(player, honorOnly);
            });

        var store = RunSavedDataStore.For("foreve");
        _maxHonorData = store.Register<MaxHonorTracker>(
            ForeveDataKeys.MaxHonorReached,
            () => new MaxHonorTracker { Value = 0 });
        SecondaryResourceHook.RegisterGlobalListener(new HonorMaxTracker(boundDef));
    }

    /// <summary>
    /// 荣誉行的显示玩家：单玩家局 = 当前玩家；双人模式 = 奥吉尔玩家（无论主/副），
    /// 保证荣誉行跟随奥吉尔角色而不是 RitsuLib 刷新的主玩家。
    /// </summary>
    private static Player? ResolveHonorDisplayPlayer(Player? current)
    {
        if (!DualCharacterState.Enabled || current?.RunState is not RunState rs) return current;
        foreach (var p in rs.Players)
        {
            if (p?.Character is OgierCharacter)
                return p;
        }
        return current;
    }

    internal static bool HasMaxHonorReached(RunState runState, int threshold)
    {
        if (_maxHonorData == null) return false;
        return _maxHonorData.TryGet(runState, out var tracker) && tracker.Value >= threshold;
    }

    private sealed class HonorMaxTracker(SecondaryResourceDefinition honorDef) : ISecondaryResourceHookListener
    {
        public decimal ModifySecondaryResourceGain(SecondaryResourceContext context, decimal amount)
        {
            if (_maxHonorData == null) return amount;
            if (context.Definition != honorDef) return amount;

            var current = SecondaryResourceCmd.Get(context.Player, honorDef.Id);
            var max = SecondaryResourceCmd.GetMax(context.Player, honorDef.Id);
            if (max == null) return amount;

            var available = max.Value - current;
            if (available <= 0) return 0;
            return Math.Min(amount, available);
        }

        public Task AfterSecondaryResourceChanged(SecondaryResourceChangeContext context)
        {
            if (_maxHonorData == null) return Task.CompletedTask;
            if (context.Definition != honorDef) return Task.CompletedTask;
            if (context.Player.RunState is not RunState runState) return Task.CompletedTask;

            _maxHonorData.Modify(runState, t =>
            {
                if (context.NewAmount > t.Value)
                    t.Value = context.NewAmount;
            });
            return Task.CompletedTask;
        }
    }
}
