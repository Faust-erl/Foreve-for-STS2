using System;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using Logger = MegaCrit.Sts2.Core.Logging.Logger; // 消除 Godot.Logger 歧义

namespace Foreve.Scripts.Patches;

/// <summary>
/// 卡牌/遗物描述中能量图标尺寸修复（2026-08-16 用户反馈）。
///
/// 需求：
///   - 卡牌描述中的能量图标缩小到原本的 0.3 倍（32x32 → 10x10，divisor=3）；
///   - 遗物描述中的能量图标缩小到原本的 0.3 倍（32x32 → 10x10，divisor=3）；
///   - 例如先史之民的奖励（轰鸣海螺等）里，能量图标过大导致描述文字被自动缩小。
///
/// ── 根因（IL/反编译实证）────────────────────────────────────────────
/// 描述文本里的能量图标由 SmartFormat 扩展 EnergyIconsFormatter.TryEvaluateFormat 输出：
///   [img]res://images/packed/sprite_fonts/{prefix}_energy_icon.png[/img]
/// 裸 [img] 不带尺寸 → Godot RichTextLabel 按纹理原生像素渲染（mod 图标实测 32x32）→
/// 在卡片/悬停提示/事件选项等小字号描述里显得巨大，MegaRichTextLabel 的
/// SetTextAutoSize/AdjustFontSize 自动缩小字号把文字压得很小。
/// 旧修复（DualCharacterRelicDescPatch，只改 NHoverTipSet 悬停提示的 5x5）覆盖不全：
/// 先史之民的奖励等事件把遗物描述原文复制进事件选项文本（RelicModel.DynamicEventDescription，
/// 走 LocString.GetFormattedText，不经悬停提示）→ 仍显示原生尺寸。
///
/// ── 方案 ────────────────────────────────────────────────────────────
/// patch LocString.GetFormattedText()（所有本地化文本的唯一格式化漏斗：
///   卡牌 GetDescriptionForPile / 遗物 DynamicDescription / 事件选项 / 悬停提示 HoverTip
///   构造全部汇入）Postfix 按 LocTable 区分：
///   - "cards" / "enchantments"（附魔附加卡面文本）：能量图标 → 纹理尺寸的 0.3 倍（32x32 → 10x10，divisor=3）；
///   - "relics" / "ancients" / "static_hover_tips"：能量图标 → 纹理尺寸的 0.3 倍（32x32 → 10x10，divisor=3）。
///     注意：先史之民的奖励（Ancient 事件）的遗物选项描述走 EventOption.FromRelic →
///     EventModel.GetOptionDescription，LocString 表是事件的 "ancients"（IL 实证：
///     AncientEventModel.get_LocTable → "ancients"），不是遗物自身的 "relics" 表，
///     因此 "ancients" 必须与 "relics" 同规则，否则轰鸣海螺等遗物选项的图标不缩小。
///     悬停提示（HoverTipFactory.L10NStatic → "static_hover_tips"，HoverTip 构造时
///     Title/Description 走 GetFormattedText）同规则。
///   - HoverTip 能量提示的图标位是 Texture2D（不走文本 [img]）：额外 patch
///     HoverTipFactory.ForEnergyWithIconPath Postfix，把图标纹理缩小到 0.3 倍。
///   - 其他表：零变化。
/// 动态按实际纹理尺寸缩放（GD.Load 缓存命中，代价可忽略）；纹理加载失败回退
/// 16x16（卡牌）/ 10x10（遗物）。修复发生在文本进入任何标签之前 → 自动缩小字号
/// 以缩小后的内容重新计算，文字不再被压小。
/// 失败一律降级（不改文本）。单玩家/双角色局均生效（问题与模式无关）。
///
/// ⚠️ 接线（主流程统一接 Entry.cs，不要直接改 Entry.cs）：
///   Foreve.Scripts.Patches.EnergyIconSizePatch.Install(Logger);
/// </summary>
public static class EnergyIconSizePatch
{
    private static Logger _logger = null!;

    /// <summary>
    /// 只匹配能量图标的裸 [img] 标签，捕获完整路径。
    /// 原版路径形如 res://images/packed/sprite_fonts/*_energy_icon.png；
    /// RitsuLib 会把 mod 角色/遗物池的路径替换为
    /// res://Foreve/Assets/UI/energy_*_text.png 等自定义路径，因此按路径中是否含 energy 判定。
    /// </summary>
    private static readonly Regex EnergyImgTagRegex = new(
        @"\[img\]([^\[\]]*energy[^\[\]]*\.png)\[/img\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>纹理加载失败时的回退尺寸：卡牌/遗物 = 32 * 0.3 ≈ 10。</summary>
    private const int CardFallbackSize = 10;
    private const int RelicFallbackSize = 10;

    public static void Install(Logger logger)
    {
        _logger = logger;
        var harmony = new Harmony("foreve.energy_icon_size");

        var getFormattedText = AccessTools.DeclaredMethod(typeof(LocString), "GetFormattedText");
        if (getFormattedText == null)
        {
            _logger.Warn("[Foreve][Dual] LocString.GetFormattedText NOT FOUND - skip energy icon size fix");
            return;
        }

        harmony.Patch(getFormattedText, postfix: new HarmonyMethod(
            typeof(EnergyIconSizePatch).GetMethod(nameof(GetFormattedTextPostfix),
                BindingFlags.Static | BindingFlags.NonPublic)));

        // HoverTip 能量提示的图标位（Texture2D，不走文本 [img]）：轰鸣海螺等遗物的
        // ExtraHoverTips = HoverTipFactory.ForEnergy(relic) → ForEnergyWithIconPath（IL 实证，
        // HoverTip 构造时 Title/Description 走 GetFormattedText，Icon 直接存 Texture2D）。
        // 所有 ForEnergy*(Card/Potion/Power/Player/Relic) 重载都汇聚到这一个私有方法，patch 一次全覆盖。
        var forEnergyInstalled = false;
        foreach (var m in typeof(HoverTipFactory).GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (m.Name != "ForEnergyWithIconPath") continue;
            harmony.Patch(m, postfix: new HarmonyMethod(
                typeof(EnergyIconSizePatch).GetMethod(nameof(ForEnergyWithIconPathPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic)));
            forEnergyInstalled = true;
            break;
        }

        _logger.Info($"[Foreve][Dual] 卡牌/遗物描述能量图标尺寸 patch 已装 (GetFormattedText=True, 卡牌=0.3x, 遗物=0.3x, HoverTip能量图标={forEnergyInstalled})");
    }

    // ── HoverTip 能量提示图标位缩小（0.3x） ────────────────────────────────

    /// <summary>HoverTip（struct）的 Icon backing field（set_Icon 是 private）。</summary>
    private static readonly FieldInfo? HoverTipIconField =
        AccessTools.Field(typeof(HoverTip), "<Icon>k__BackingField");

    /// <summary>
    /// ForEnergyWithIconPath Postfix：把能量提示的 Texture2D 图标缩小为 min(w,h)/3（32x32 → 10x10）。
    /// 与文本 [img] 规则一致（纹理尺寸的 0.3 倍）。GetImage 返回解码拷贝，Resize 安全。
    /// </summary>
    private static void ForEnergyWithIconPathPostfix(ref IHoverTip __result)
    {
        try
        {
            if (HoverTipIconField == null || __result is not HoverTip ht) return;
            var icon = ht.Icon;
            if (icon == null) return;
            var min = Math.Min(icon.GetWidth(), icon.GetHeight());
            if (min <= 0) return;
            var target = Math.Max(1, min / 3);
            if (Math.Max(icon.GetWidth(), icon.GetHeight()) <= target) return;

            var img = icon.GetImage();
            if (img == null) return;
            img.Resize(target, target, Image.Interpolation.Bilinear);
            var small = ImageTexture.CreateFromImage(img);
            if (small == null) return;
            HoverTipIconField.SetValue(ht, small);
            __result = ht;
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] HoverTip 能量图标缩小失败: {e.Message}");
        }
    }

    /// <summary>
    /// 格式化文本出口：按 LocTable 把能量图标裸 [img] 改写为带尺寸标签。
    /// cards/enchantments 与 relics/ancients/static_hover_tips 均 → 0.3x；其余表零变化。
    /// optimizer：先按表名过滤再扫描文本——GetFormattedText 是全游戏文本漏斗，
    /// 绝大多数调用不属于目标表，避免对每段文本做子串扫描。
    /// </summary>
    private static void GetFormattedTextPostfix(LocString __instance, ref string __result)
    {
        try
        {
            if (__instance == null || string.IsNullOrEmpty(__result)) return;

            var divisor = __instance.LocTable switch
            {
                "cards" or "enchantments" => 3, // 卡牌描述（含附魔附加卡面文本）：纹理尺寸的 0.3 倍（32x32 → 10x10）
                "relics" or "ancients" or "static_hover_tips" => 3, // 遗物描述 + 先史事件选项 + 悬停提示：纹理尺寸的 0.3 倍（32x32 → 10x10）
                _ => 0,
            };
            if (divisor <= 0) return;

            // 热路径短截：目标表文本中绝大多数也不含能量图标
            // 不能用 _energy_icon.png 判断：mod 能量图标路径可能是 energy_shared_text.png 等。
            if (__result.IndexOf("energy", StringComparison.OrdinalIgnoreCase) < 0) return;
            if (!EnergyImgTagRegex.IsMatch(__result)) return;

            __result = EnergyImgTagRegex.Replace(__result, m => ReplaceEnergyTag(m, divisor));
        }
        catch (Exception e)
        {
            _logger?.Warn($"[Foreve][Dual] fix energy icon size failed: {e.Message}");
        }
    }

    /// <summary>把单个能量图标 [img] 标签改写为 [img={w}x{h}]：w/h = 实际纹理尺寸 ÷ divisor。</summary>
    private static string ReplaceEnergyTag(Match m, int divisor)
    {
        var path = m.Groups[1].Value;
        var size = divisor == 2 ? CardFallbackSize : RelicFallbackSize;
        try
        {
            var tex = GD.Load<Texture2D>(path);
            if (tex != null)
            {
                size = Math.Max(1, Math.Min(tex.GetWidth(), tex.GetHeight()) / divisor);
            }
        }
        catch
        {
            // 加载失败用回退尺寸
        }
        return $"[img={size}x{size}]{path}[/img]";
    }
}
