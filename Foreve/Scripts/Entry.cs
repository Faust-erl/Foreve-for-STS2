using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2RitsuLib;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Utils;
using Foreve.Scripts.Characters.Ogier;

namespace Foreve.Scripts;

[ModInitializer(nameof(Init))]
public class Entry
{
    public const string ModId = "foreve";
    public static readonly Logger Logger = RitsuLibFramework.CreateLogger(ModId);

    public static void Init()
    {
        var assembly = Assembly.GetExecutingAssembly();
        RitsuLibFramework.EnsureGodotScriptsRegistered(assembly, Logger);

        // 模型能力持久化系统初始化（必须在任何卡牌模型实例化前——RebelBlade 构造器
        // 用 ModelCapabilities.Get(model).Apply() 挂动态减费能力，未初始化时 Apply 抛
        // InvalidOperationException "persistence has not been registered" 导致启动崩溃，2026-08-12 实战）
        STS2RitsuLib.Models.Capabilities.ModelCapabilities.EnsureInitialized();

        // localization 优先从 DLL 内嵌资源加载（Foreve.Foreve.localization.{lang}.json），
        // PCK 作为兜底。这样只改 JSON + dotnet build 即可生效，不必重打 foreve.pck。
        var i18n = RitsuLibFramework.CreateModLocalization(
            ModId,
            ModId,
            null,
            new[] { "Foreve.Foreve.localization" },
            new[] { "res://Foreve/localization" },
            assembly
        );

        var keys = i18n.GetAllKeys().ToList();
        Logger.Info($"[Foreve] I18N loaded {keys.Count} keys for locale '{I18N.ResolveCurrentLanguageCode()}'");
        Logger.Info($"[Foreve] Available languages: {string.Join(", ", i18n.EnumerateAvailableLanguages())}");

        foreach (var stem in new[] { "cards", "powers", "relics", "characters", "potions", "honors", "events" })
        {
            RitsuLibFramework.RegisterI18NLocTableBridge(ModId, i18n, stem);
        }

        // 注入 I18N 实例到 Harmony patch，直接拦截 LocTable.GetRawText
        LocTableI18NInjection.ForeveI18N = i18n;
        var harmony = new Harmony("foreve.loc_table_injection");
        harmony.PatchAll(typeof(LocTableI18NInjection).Assembly);
        Logger.Info("[Foreve] LocTable I18N injection patch applied");

        ModTypeDiscoveryHub.RegisterModAssembly(ModId, assembly);

        // 注册奥吉尔荣誉系统（必须在类型发现之后，否则角色类型匹配可能失败）
        OgierCharacter.RegisterHonor();
        Logger.Info("[Foreve] Ogier honor secondary resource registered");

        // 注册银钥全局资源（同样必须在类型发现之后）
        Foreve.Scripts.SilverKey.SilverKeyResource.RegisterSilverKey();
        Logger.Info("[Foreve] Silver key secondary resource registered");

        // 修复游戏原版 bug：放弃时 Kill 状态机 GameOver 分支的 StopMusic 在 RunMusicController 为 null 时 NRE 卡死
        Foreve.Scripts.Patches.KillStopMusicFix.Install(Logger);
        Foreve.Scripts.Patches.CharSelectBgPositionFix.Install(Logger);

        // 事件进入崩溃兜底：EventOption.AddLocVars 在 Owner.Character 为 null 时 NRE（mod 事件触发）
        Foreve.Scripts.Patches.EventOptionAddLocVarsFix.Install(Logger);

        // 打牌动作动画：订阅 CardPlayingEvent，攻击牌播 attack、其余播 defense（播完回待机）
        Foreve.Scripts.Combat.PlayerCardAnimationController.EnsureInstalled();

        // 受击/通关动画：订阅 CurrentHpChangedEvent（我方掉血播 hurt）与 CombatVictoryEvent（胜利播 victory）
        Foreve.Scripts.Combat.CombatCharacterAnimationController.EnsureInstalled();

        // 双角色模式（批次 1a）：选人两步流程（4 池自选1+发现3）+ 开局注入第二玩家 + RunStartedEvent 资源合并
        Foreve.Scripts.Patches.DualCharacterSelectPatches.Install(Logger);
        Foreve.Scripts.Patches.DualCharacterRunStartPatches.Install(Logger);
        Foreve.Scripts.DualCharacter.DualCharacterRunMerge.EnsureInstalled();

        // 双角色模式（2026-08-18 遗物系统重做）：遗物「装备角色」——角色向遗物获得时
        // 弹头像二选一指定装备角色（死亡角色置灰不可选），初始遗物自动绑定所属角色；
        // 效果按装备者结算（原版遗物 Owner.Creature 重定向 + mod 遗物按记录解析）。
        // 必须晚于 DualCharacterRunMerge.EnsureInstalled()（读档恢复依赖 MainPlayer 已写入）。
        Foreve.Scripts.DualCharacter.DualCharacterRelicEquip.EnsureInstalled();

        // 双角色模式（批次 1b）：战斗核心 —— GetMe 主玩家/副玩家自动 ready/主玩家死亡保护/敌人随机单目标+意图标记/奖励1份/关闭多人缩放
        Foreve.Scripts.Patches.DualCharacterCombatPatches.Install(Logger);

        // 战斗结束费用还原：桀骜之刃等每局战斗内会改变费用的牌，统一在战斗结束后将费用还原
        Foreve.Scripts.Combat.CombatCostReset.EnsureInstalled();

        // 双角色模式（2026-08-16）：非主副角色技能牌/能力牌（无色牌/其他角色牌）出牌时弹窗指定目标角色
        Foreve.Scripts.Patches.DualCharacterCardTargetPatch.Install(Logger);

        // 双角色模式（实测修复）：卡牌伤害/格挡预览数值按卡牌所属角色结算
        // （另一名角色的活力/力量/敏捷不再错误加成到本角色卡牌数值）
        Foreve.Scripts.Patches.DualCharacterCardValuePatch.Install(Logger);

        // 双角色模式（实测修复）：能量共用 —— 副玩家能量增减重定向到主玩家共用能量（播种附魔/巨兽暴走等）
        Foreve.Scripts.Patches.DualCharacterEnergyPatch.Install(Logger);

        // 双角色模式（实测修复）：奥吉尔荣誉 UI/效果与主副角色身份解耦
        Foreve.Scripts.Patches.DualCharacterHonorUiPatch.Install(Logger);

        // 双角色模式（批次 2a）：复活管理（ReviveCharacter API/篝火随进阶复活/新层满血复活）+ 事件掉血自选承受
        Foreve.Scripts.DualCharacter.DualCharacterRevive.Install(Logger);
        Foreve.Scripts.Patches.DualCharacterEventPatches.Install(Logger);

        // 双角色模式（实测修复）：合作事件投票同步（副角色自动跟随主角色选择）
        Foreve.Scripts.Patches.DualCharacterSharedEventVotePatch.Install(Logger);

        // 双角色模式（批次 3）：副玩家联机面板隐藏 + 意图描述单目标化
        Foreve.Scripts.Patches.DualCharacterUiPolishPatch.Install(Logger);

        // 双角色模式（批次 2c）：篝火「复活」选项（取代原版联机「给队友回血」MEND 选项）
        Foreve.Scripts.Patches.DualCharacterRestSiteRevivePatch.Install(Logger);

        // 双角色模式（实测修复）：顶栏双血条 + 地图投票同步（副玩家跟随主玩家）
        Foreve.Scripts.Patches.DualCharacterTopBarMapPatch.Install(Logger);

        // 双角色模式（实测修复）：奖励/商店卡池合并主+副角色卡池
        Foreve.Scripts.Patches.DualCharacterRewardPoolsPatch.Install(Logger);

        // 双角色模式（实测修复）：宝箱只出 1 个遗物给主玩家选择，副玩家不自动分配
        Foreve.Scripts.Patches.DualCharacterTreasurePatch.Install(Logger);

        // 双角色模式（实测修复）：Boss 后换层投票同步（副玩家自动跟随主玩家，不卡「等待其他玩家」）
        Foreve.Scripts.Patches.DualCharacterActChangePatch.Install(Logger);

        // 双角色模式（实测修复）：遗物描述能量图标尺寸（旧实现已由 EnergyIconSizePatch 取代）
        Foreve.Scripts.Patches.EnergyIconSizePatch.Install(Logger);

        Logger.Info("[Foreve] 忘却前夜 mod 初始化完成");
    }
}
