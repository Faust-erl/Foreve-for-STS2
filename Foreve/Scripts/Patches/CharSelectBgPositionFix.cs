using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace Foreve.Scripts.Patches;

/// <summary>选人界面背景大图右对齐后左移：mod 角色的选人背景（rotan_bg/ogier_bg/dore_bg 场景）
/// 挂在游戏 AnimatedBg 层（offset 偏左 -388），显示偏左、右侧不贴屏幕边缘。
/// 选中角色后把背景 Sprite2D 右移，使图片右缘贴屏幕右缘后再左移 114px（给右侧文字留空间）。</summary>
public static class CharSelectBgPositionFix
{
    private static MegaCrit.Sts2.Core.Logging.Logger Logger = null!;

    public static void Install(MegaCrit.Sts2.Core.Logging.Logger logger)
    {
        Logger = logger;
        var harmony = new Harmony("foreve.char_select_bg_position");

        var patched = 0;
        // 手动选角路径
        var select = typeof(NCharacterSelectScreen).GetMethod("SelectCharacter");
        if (select != null)
        {
            harmony.Patch(select, postfix: new HarmonyMethod(
                typeof(CharSelectBgPositionFix).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
            patched++;
        }
        // 随机选角路径
        var random = typeof(NCharacterSelectScreen).GetMethod("OnLocalCharacterChangedForRandom", BindingFlags.Instance | BindingFlags.NonPublic);
        if (random != null)
        {
            harmony.Patch(random, postfix: new HarmonyMethod(
                typeof(CharSelectBgPositionFix).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
            patched++;
        }

        if (patched > 0) Logger.Info($"[Foreve][UI] Char select bg right-align installed ({patched} targets)");
        else Logger.Warn("[Foreve][UI] SelectCharacter NOT FOUND - skip bg position fix");
    }

    private static void Postfix(NCharacterSelectScreen __instance)
    {
        try
        {
            var bgContainer = __instance.GetNodeOrNull<Control>("AnimatedBg");
            if (bgContainer == null)
            {
                // 兜底：遍历整个屏幕找名字含 bg 的容器（结构变了也能修）
                foreach (var n in __instance.GetChildren())
                {
                    if (n is Control c && c.Name.ToString().Contains("Bg", StringComparison.OrdinalIgnoreCase))
                    {
                        bgContainer = c;
                        break;
                    }
                }
                if (bgContainer == null)
                {
                    Logger.Info("[Foreve][UI] AnimatedBg not found in NCharacterSelectScreen");
                    return;
                }
                Logger.Info($"[Foreve][UI] AnimatedBg fallback found: {bgContainer.Name}");
            }

            var screenW = __instance.GetViewportRect().Size.X;
            if (screenW <= 0) return;

            var moved = 0;
            var diagnostics = new System.Collections.Generic.List<string>();
            foreach (var child in bgContainer.GetChildren())
            {
                // 官方挂载节点名 = {角色Id}_bg（SelectCharacter IL：Concat(Entry, "_bg")）；根是 Control（Instantiate<Control> 强转）
                var name = child.Name.ToString();
                if (!name.Contains("OGIER", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("ROTAN", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("DORE", StringComparison.OrdinalIgnoreCase))
                    continue;
                diagnostics.Add($"{name}({child.GetType().Name})");

                // mod bg 场景根 = Control，图在内部任意层级：递归找 Sprite2D/TextureRect 右对齐
                moved += AlignRight(child, screenW);
            }

            if (moved > 0)
                Logger.Info($"[Foreve][UI] char select bg right-aligned {moved} node(s) (screen {screenW:F0})");
            else
                Logger.Info($"[Foreve][UI] char select bg: no node moved. AnimatedBg children: {string.Join(", ", diagnostics)}");
        }
        catch (Exception e)
        {
            Logger.Warn($"[Foreve][UI] bg position fix failed: {e.Message}");
        }
    }

    /// <summary>递归找节点树中的 Sprite2D/TextureRect 并右对齐：图右缘贴屏幕右缘（全局坐标）。</summary>
    private static int AlignRight(Node node, float screenW)
    {
        var moved = 0;
        switch (node)
        {
            case Sprite2D sprite:
            {
                var texW = sprite.Texture?.GetWidth() ?? 0;
                if (texW > 0)
                {
                    var targetX = screenW - texW - 114; // 全局 X：图右缘贴屏幕右缘后再左移 114px（右侧有文字，留空间）
                    if (Math.Abs(sprite.GlobalPosition.X - targetX) > 1f)
                    {
                        sprite.GlobalPosition = new Vector2(targetX, sprite.GlobalPosition.Y);
                        moved++;
                    }
                }
                return moved;
            }
            case TextureRect rect:
            {
                var tex = rect.Texture;
                var texW = tex?.GetWidth() ?? (int)rect.Size.X;
                if (texW > 0)
                {
                    var targetX = screenW - texW - 114; // 右对齐后再左移 114px（右侧有文字，留空间）
                    if (Math.Abs(rect.GlobalPosition.X - targetX) > 1f)
                    {
                        var gp = rect.GlobalPosition;
                        rect.GlobalPosition = new Vector2(targetX, gp.Y);
                        moved++;
                    }
                }
                return moved;
            }
        }

        foreach (var c in node.GetChildren())
            moved += AlignRight(c, screenW);
        return moved;
    }
}
