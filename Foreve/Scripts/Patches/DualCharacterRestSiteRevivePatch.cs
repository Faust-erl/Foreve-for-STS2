using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Runs;
using Foreve.Scripts.DualCharacter;

namespace Foreve.Scripts.Patches;

/// <summary>
/// 双角色模式：篝火「复活」选项（批次 2c，用户拍板：取代原版联机「给队友回血」选项）。
/// 全部 IL 结论来自 C:\tmp\sts2_full.il（2026-08-14 实证）。
///
/// 原版篝火选项机制（调研结论）：
///   1. 选项构建：RestSiteOption.Generate(Player)（static，IL 1748648）——每名玩家固定
///      [HealRestSiteOption(HEAL, 自回血), SmithRestSiteOption(SMITH, 磨牌)]；
///      当 player.RunState.Players.Count > 1 时追加 MendRestSiteOption(MEND, IL 1747663，
///      = 联机「给队友回血」选项，悬停队友角色节点选目标，GetHealAmount 复用自回血数值)。
///   2. 列表消费：BeginRestSite（IL 1077296）把 Generate 返回值存入
///      RestSiteSynchronizer._restSites[i].options；UI 渲染 NRestSiteRoom.UpdateRestSiteOptions
///      （IL 773533）经 GetLocalOptions（IL 1077727）读同一列表；点击执行
///      ChooseOption d__28.MoveNext（IL 1078036）从列表按索引取选项实例调 OnSelect()。
///      → 在 Generate 返回的列表里把 MEND 换成我们的复活选项，UI 渲染与点击执行全链路自动生效，
///      无需 patch 同步器/UI。
///   3. 选项禁用机制：RestSiteOption.IsEnabled（virtual，base 恒 true，IL 1748580）；
///      NRestSiteButton.Create（IL 782552）用 !option.IsEnabled 置 _isUnclickable
///      （置灰 + OnRelease 直接 return 不可点，IL 782656）→ 覆写 IsEnabled 即可实现
///      「无人死亡时禁用」。
///   4. 本地化：RestSiteOption.Title/Description 为 virtual LocString（base 按
///      rest_site_ui / OPTION_{OptionId}.name 拼接）；mod 的 LocTableI18NInjection 按
///      FOREVE_ 前缀注入任意 LocString 查询 → 自定义键走扁平 zhs.json/eng.json。
///
/// 本 patch 实现：
///   - Harmony Postfix 拦截 RestSiteOption.Generate(Player)：双人模式下把列表中的
///     MendRestSiteOption 替换为 ForeveRestSiteReviveOption（原地替换，引用共享给
///     _restSites，UI 与执行同源）。
///   - ForeveRestSiteReviveOption（RestSiteOption 子类）：OptionId=FOREVE_REVIVE，
///     Title/Description 走 FOREVE_REST_REVIVE.* 本地化键（Title 非 virtual，靠 getter Postfix）；
///     IsEnabled=双人模式且有死亡角色；OnSelect=对全部死亡角色调 DualCharacterRevive.ReviveCharacter
///     (player, RestSiteRevivePercent(进阶))（批次 2a 已实证：战斗外 CreatureCmd.Heal 可用、
///     HealInternal 自动恢复 hooks）。图标使用 mod 自定义素材 复活.png（get_Icon Postfix）。
///
/// ⚠️ 接线（主流程统一在 Entry.cs 处理，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.DualCharacterRestSiteRevivePatch.Install(Logger);
///   批次 2a 的进篝火自动复活（DualCharacterRevive.OnRoomEntered）已移除；
///   新层满血复活（ActEnteredEvent）保留在 DualCharacterRevive.cs。
/// </summary>
public static class DualCharacterRestSiteRevivePatch
{
    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        var harmony = new Harmony("foreve.dual_character_rest_site_revive");

        var generate = AccessTools.Method(typeof(RestSiteOption), "Generate", new[] { typeof(Player) });
        if (generate == null)
        {
            logger.Warn("[Foreve][Dual] RestSiteOption.Generate NOT FOUND —— 篝火复活选项不可用");
            return;
        }
        harmony.Patch(generate, postfix: new HarmonyMethod(
            typeof(DualCharacterRestSiteRevivePatch).GetMethod(nameof(GeneratePostfix), BindingFlags.Static | BindingFlags.NonPublic)));

        // RestSiteOption.Title/Icon 在 sts2.dll 中不是 virtual（IL 1748514/1748580 无 newslot virtual），
        // 无法 override —— 用 getter Postfix 替换标题与图标。
        var title = AccessTools.PropertyGetter(typeof(RestSiteOption), nameof(RestSiteOption.Title));
        harmony.Patch(title, postfix: new HarmonyMethod(
            typeof(DualCharacterRestSiteRevivePatch).GetMethod(nameof(TitlePostfix), BindingFlags.Static | BindingFlags.NonPublic)));

        var icon = AccessTools.PropertyGetter(typeof(RestSiteOption), nameof(RestSiteOption.Icon));
        harmony.Patch(icon, postfix: new HarmonyMethod(
            typeof(DualCharacterRestSiteRevivePatch).GetMethod(nameof(IconPostfix), BindingFlags.Static | BindingFlags.NonPublic)));

        // 2026-08-23 修复：篝火休息选项在双人模式下只给主角色回血，副角色没回血
        // HealRestSiteOption.OnSelect() 执行休息回血，需要 postfix 给副角色也回血
        var onSelect = AccessTools.Method(typeof(HealRestSiteOption), nameof(HealRestSiteOption.OnSelect));
        if (onSelect != null)
        {
            harmony.Patch(onSelect, postfix: new HarmonyMethod(
                typeof(DualCharacterRestSiteRevivePatch).GetMethod(nameof(HealRestSiteOptionPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            logger.Info("[Foreve][Dual] 篝火休息选项双人回血 patch 已安装");
        }
        else
        {
            logger.Warn("[Foreve][Dual] HealRestSiteOption.OnSelect NOT FOUND");
        }

        logger.Info("[Foreve][Dual] 篝火复活选项已安装（双人模式：给队友回血 → 复活）");
    }

    /// <summary>
    /// Passthrough Postfix：双人模式下，篝火休息选项给主角色回血后，也给副角色按
    /// 副角色自己的生命上限比例回血。返回类型与首个参数一致（Task&lt;bool&gt;），
    /// Harmony 会把它作为 passthrough postfix 等待完成——保证 ChooseOption 拿到
    /// 结果前副角色回血已经执行完，而不是游离的 async void 后台任务。
    /// HealRestSiteOption.OnSelect() 原方法只给 Owner 回血。
    /// </summary>
    private static async Task<bool> HealRestSiteOptionPostfix(Task<bool> __result, RestSiteOption __instance)
    {
        var succeeded = await __result; // 原方法失败/异常时原样向上传播
        try
        {
            if (!succeeded) return succeeded;

            // Owner 属性是 protected，静态方法内需要通过反射获取
            var ownerProperty = typeof(RestSiteOption).GetProperty("Owner", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (ownerProperty == null)
            {
                GD.Print("[Foreve][Dual] Owner property not found");
                return succeeded;
            }
            var owner = ownerProperty.GetValue(__instance) as Player;
            if (owner == null) return succeeded;
            if (!DualCharacterState.IsDualMode(owner.RunState as RunState)) return succeeded;

            // 获取副玩家
            var secondary = DualCharacterState.SecondaryPlayer;
            if (secondary == null || secondary.Creature == null) return succeeded;
            if (secondary.Creature.IsDead) return succeeded; // 死亡角色不通过休息回血（用复活选项）

            // 确保副玩家不是主玩家（理论上不应该相同，但防御性检查）
            if (ReferenceEquals(secondary, owner)) return succeeded;

            // 副角色按自己的生命上限比例计算治疗量（30% MaxHp，并保留游戏对
            // 休息治疗量的 Hook.ModifyRestSiteHealAmount 修正），而不是套用主角色数值。
            var healAmount = HealRestSiteOption.GetHealAmount(secondary);

            // 给副玩家回血
            await CreatureCmd.Heal(secondary.Creature, healAmount, true);
            GD.Print($"[Foreve][Dual] 篝火休息：副角色也回血 {healAmount} HP");
        }
        catch (Exception e)
        {
            // 副角色治疗失败不吞掉原方法的成功结果：主角色已正常休息，日志保留现场。
            GD.Print($"[Foreve][Dual] 篝火休息副角色回血失败: {e}");
        }
        return succeeded;
    }

    /// <summary>
    /// Postfix：双人模式下把 Generate 返回列表里的 MendRestSiteOption（联机「给队友回血」）
    /// 替换为 ForeveRestSiteReviveOption。列表引用被 BeginRestSite 存入
    /// _restSites[i].options，原地替换即同时作用于 UI 渲染与 ChooseOption 执行。
    /// </summary>
    private static void GeneratePostfix(Player player, List<RestSiteOption> __result)
    {
        try
        {
            if (player == null || __result == null) return;
            if (!DualCharacterState.IsDualMode(player.RunState as RunState)) return;

            for (var i = 0; i < __result.Count; i++)
            {
                if (__result[i] is MendRestSiteOption)
                {
                    __result[i] = new ForeveRestSiteReviveOption(player);
                    GD.Print("[Foreve][Dual] 篝火选项：给队友回血(MEND) → 复活(FOREVE_REVIVE)");
                }
            }
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 篝火复活选项替换失败: {e}");
        }
    }

    /// <summary>
    /// get_Title Postfix：复活选项标题走 mod 本地化（RestSiteOption.Title 非 virtual，只能 patch）。
    /// </summary>
    private static void TitlePostfix(RestSiteOption __instance, ref LocString __result)
    {
        if (__instance is ForeveRestSiteReviveOption)
        {
            // 注意：裸 "foreve_I18N" 表未注册会抛 LocException（DualCharacterSelectPatches 已实测），
            // 这里复用已注册的 foreve_I18N_cards 表；文本仍由 LocTableI18NInjection 按 FOREVE_ 键注入。
            __result = new LocString("foreve_I18N_cards", "FOREVE_REST_REVIVE.title");
        }
    }

    /// <summary>复活选项插图：PCK 内 res:// 路径（素材已放入 Foreve/Assets/UI/复活.png）。</summary>
    private const string ReviveIconResPath = "res://Foreve/Assets/UI/复活.png";

    /// <summary>
    /// 复活选项插图：美术源目录绝对路径。公开版不再依赖本机绝对路径，
    /// 资源已放在仓库内 Foreve/Assets/UI/复活.png，PCK 与 mod 部署目录均可加载。
    /// </summary>
    private const string ReviveIconSourcePath = "";

    /// <summary>
    /// get_Icon Postfix：自定义 OptionId 无对应素材，复活选项改用 mod 自定义素材 复活.png
    /// （RestSiteOption.Icon 非 virtual，只能 patch）。
    /// 加载顺序：PCK res:// → （可选）美术源绝对路径 → mod 部署目录 Assets/UI → 原版 mend 图标兜底。
    /// </summary>
    private static void IconPostfix(RestSiteOption __instance, ref Texture2D __result)
    {
        if (__instance is ForeveRestSiteReviveOption)
        {
            __result = GetReviveIcon();
        }
    }

    private static Texture2D? _cachedReviveIcon;

    private static Texture2D GetReviveIcon()
    {
        if (_cachedReviveIcon != null) return _cachedReviveIcon;

        // 1) PCK 内打包路径（后续素材导入 Foreve/Assets/UI 后可用）
        try
        {
            if (ResourceLoader.Exists(ReviveIconResPath))
            {
                var texture = GD.Load<Texture2D>(ReviveIconResPath);
                if (texture != null)
                {
                    _cachedReviveIcon = texture;
                    return texture;
                }
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][Dual] 复活插图 res:// 加载失败: {ex.Message}");
        }

        // 2) 兼容旧的本地素材源目录（公开版 ReviveIconSourcePath 为空，通常走第 3 步）
        var source = LoadAbsoluteTexture(ReviveIconSourcePath);
        if (source != null)
        {
            _cachedReviveIcon = source;
            return source;
        }

        // 3) mod 部署目录兜底（构建时 CopyMod 会把素材复制到 mods\Foreve\Assets\UI）
        try
        {
            var dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrWhiteSpace(dllDir))
            {
                source = LoadAbsoluteTexture(Path.Combine(dllDir, "Assets", "UI", "复活.png"));
                if (source != null)
                {
                    _cachedReviveIcon = source;
                    return source;
                }
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][Dual] 复活插图 mod 目录加载失败: {ex.Message}");
        }

        // 4) 原版 mend（给队友回血）图标兜底
        GD.Print("[Foreve][Dual] 复活插图缺失，使用 option_mend.png 兜底");
        _cachedReviveIcon = PreloadManager.Cache.GetTexture2D("ui/rest_site/option_mend.png");
        return _cachedReviveIcon;
    }

    private static Texture2D? LoadAbsoluteTexture(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var image = Image.LoadFromFile(path);
            if (image == null) return null;
            return ImageTexture.CreateFromImage(image);
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][Dual] 复活插图绝对路径加载失败 {path}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// 篝火「复活」选项：点击后复活所有死亡角色（双人局死亡角色最多 1 个，双死判负），
/// 恢复百分比 = DualCharacterRevive.RestSiteRevivePercent(进阶)（100−5×A，钳 [50,100]）。
/// 无人死亡时 IsEnabled=false → 原版按钮置灰不可点。
/// </summary>
public sealed class ForeveRestSiteReviveOption : RestSiteOption
{
    public ForeveRestSiteReviveOption(Player owner) : base(owner) { }

    public override string OptionId => "FOREVE_REVIVE";

    // Title/Icon 非 virtual 无法 override —— 由 DualCharacterRestSiteRevivePatch 的
    // get_Title/get_Icon Postfix 提供（mod 本地化标题 + 自定义复活.png 图标）。

    public override LocString Description => new LocString("foreve_I18N_cards", "FOREVE_REST_REVIVE.description");

    public override IEnumerable<string> AssetPaths => new[] { "ui/rest_site/option_mend.png" };

    public override bool IsEnabled
    {
        get
        {
            var runState = Owner?.RunState;
            if (!DualCharacterState.IsDualMode(runState as RunState)) return false;
            return runState.Players.Any(p => p?.Creature != null && p.Creature.IsDead);
        }
    }

    public override async Task<bool> OnSelect()
    {
        try
        {
            var runState = Owner?.RunState;
            if (!DualCharacterState.IsDualMode(runState as RunState)) return false;

            var dead = runState.Players.Where(p => p?.Creature != null && p.Creature.IsDead).ToList();
            if (dead.Count == 0)
            {
                GD.Print("[Foreve][Dual] 篝火复活：没有需要复活的角色，忽略");
                return false;
            }

            var percent = DualCharacterRevive.RestSiteRevivePercent(runState.AscensionLevel);
            foreach (var p in dead)
            {
                await DualCharacterRevive.ReviveCharacter(p, percent);
            }
            return true;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 篝火复活执行失败: {e}");
            return false;
        }
    }
}
