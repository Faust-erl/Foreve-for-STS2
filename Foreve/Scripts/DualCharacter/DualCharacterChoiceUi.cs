using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace Foreve.Scripts.DualCharacter;

/// <summary>
/// 双角色模式共用头像选择弹窗（2026-08-16 从 DualCharacterEventPatches 提取共享）：
/// 事件掉血/失去最大生命自选承受者、无色/其他角色技能牌能力牌指定目标角色，共用同一
/// 纯代码 Godot 弹窗（CanvasLayer + 半透明遮罩 + 角色头像按钮，TaskCompletionSource 等待）。
///
/// 调研结论（批次 2a，IL 实证）：游戏/RitsuLib 无现成的「选一名玩家」弹窗 API，
/// 最简可行方案 = mod 自建纯代码弹窗，零场景资源、零本地化依赖。
/// 尺寸规格（2026-08-15 用户需求）：头像 = 左上角角色头像（48×48）的 2.5 倍 = 120×120，
/// 标题字号同步放大 2.5 倍；悬停提亮 1.22 倍。
/// </summary>
public static class DualCharacterChoiceUi
{
    /// <summary>头像悬停高亮色（比白色稍亮）。</summary>
    private static readonly Color PortraitHoverModulate = new(1.22f, 1.22f, 1.22f, 1f);

    /// <summary>头像常态色。</summary>
    private static readonly Color PortraitNormalModulate = Colors.White;

    /// <summary>死亡/不可选角色的置灰色（2026-08-18：遗物装备指定时死亡角色头像置灰且不可点击）。</summary>
    private static readonly Color PortraitDisabledModulate = new(0.45f, 0.45f, 0.45f, 0.85f);

    /// <summary>
    /// 弹窗选择一名角色：candidates 通常为主/副玩家，disabled 为展示但不可选的玩家
    /// （例如已死亡的角色——头像置灰、不响应点击），fallback 在弹窗/场景树不可用时
    /// 直接返回（不弹窗）。全部候选都不可选时直接返回 fallback，避免弹窗卡死。
    /// </summary>
    public static async Task<Creature> ShowAsync(
        string title, List<Player> candidates, List<Player> disabled, Creature fallback)
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return fallback;

        var layer = new CanvasLayer { Layer = 200 }; // 盖在事件/战斗界面之上
        tree.Root.AddChild(layer);
        try
        {
            // 半透明遮罩：拦截下层点击
            var dim = new ColorRect
            {
                Color = new Color(0f, 0f, 0f, 0.65f),
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(dim);

            // 居中面板
            var panel = new PanelContainer();
            panel.SetAnchorsPreset(Control.LayoutPreset.Center);
            panel.GrowHorizontal = Control.GrowDirection.Both;
            panel.GrowVertical = Control.GrowDirection.Both;
            layer.AddChild(panel);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 16);
            panel.AddChild(vbox);

            var titleLabel = new Label { Text = title };
            titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(titleLabel);
            ScaleTitleFont(titleLabel, 2.5f);

            // 头像按钮：左上角头像尺寸 × 2.5
            var avatarSize = FindTopLeftAvatarSize(tree);
            var portraitSize = avatarSize * 2.5f;

            var buttonRow = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
            };
            buttonRow.AddThemeConstantOverride("separation", 24);
            vbox.AddChild(buttonRow);

            var tcs = new TaskCompletionSource<Creature>();
            foreach (var p in candidates)
            {
                var portrait = ResolveCharacterPortrait(p);
                var btn = new TextureButton
                {
                    TextureNormal = portrait,
                    IgnoreTextureSize = true,
                    StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = portraitSize,
                    Size = portraitSize,
                };

                var isEnabled = disabled == null || !disabled.Contains(p);
                if (!isEnabled)
                {
                    // 死亡/不可选：置灰 + 不响应悬停/点击（2026-08-18 用户需求）
                    btn.SelfModulate = PortraitDisabledModulate;
                    btn.MouseFilter = Control.MouseFilterEnum.Ignore;
                }
                else
                {
                    // 悬停时稍微提亮头像，增强可点击反馈（进入=1.22 倍亮度，离开恢复白色）。
                    btn.MouseEntered += () =>
                    {
                        if (GodotObject.IsInstanceValid(btn))
                            btn.SelfModulate = PortraitHoverModulate;
                    };
                    btn.MouseExited += () =>
                    {
                        if (GodotObject.IsInstanceValid(btn))
                            btn.SelfModulate = PortraitNormalModulate;
                    };
                    btn.Pressed += () => tcs.TrySetResult(p.Creature!);
                }
                buttonRow.AddChild(btn);
            }

            // 全部候选不可选（理论上不会发生）：立即回退，避免永久卡死
            if (candidates.Count > 0 && candidates.All(c => disabled != null && disabled.Contains(c)))
                return fallback;

            return await tcs.Task;
        }
        finally
        {
            layer.QueueFree();
        }
    }

    /// <summary>兼容旧调用：无可选项列表等同于全部可选。</summary>
    public static Task<Creature> ShowAsync(string title, List<Player> candidates, Creature fallback)
        => ShowAsync(title, candidates, new List<Player>(), fallback);

    /// <summary>左上角角色头像的基准尺寸：运行时在场景树中找 NMultiplayerPlayerState 的
    /// %CharacterIcon；读不到时回退场景实证值 48×48（scenes/ui/multiplayer_player_state.tscn）。</summary>
    private static Vector2 FindTopLeftAvatarSize(SceneTree tree)
    {
        try
        {
            var queue = new Queue<Node>();
            queue.Enqueue(tree.Root);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (node is NMultiplayerPlayerState state)
                {
                    var icon = state.GetNodeOrNull<TextureRect>("%CharacterIcon");
                    if (icon != null && icon.Size.X >= 8f && icon.Size.Y >= 8f)
                        return icon.Size;
                }

                foreach (var child in node.GetChildren())
                    queue.Enqueue(child);
            }
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 左上角头像尺寸读取失败，使用 48×48 回退: {e.Message}");
        }
        return new Vector2(48f, 48f);
    }

    /// <summary>把标题字号放大 scale 倍（相对当前主题字号；主题读取失败时按 16px 基准）。</summary>
    private static void ScaleTitleFont(Label title, float scale)
    {
        try
        {
            var baseSize = title.GetThemeFontSize("font_size");
            if (baseSize <= 0) baseSize = 16;
            title.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(baseSize * scale));
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 弹窗标题字号放大失败: {e.Message}");
        }
    }

    /// <summary>角色头像纹理：优先 CharacterModel.IconTexture（左上角头像同款），
    /// 回退 CharacterSelectIcon（原版/选人页大图，按按钮尺寸缩放显示）。</summary>
    private static Texture2D? ResolveCharacterPortrait(Player player)
    {
        var character = player.Character;
        if (character == null) return null;

        try
        {
            var icon = character.IconTexture;
            if (icon != null) return icon;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 弹窗头像 IconTexture 读取异常: {e.Message}");
        }

        try
        {
            var selectIcon = character.CharacterSelectIcon;
            if (selectIcon != null) return selectIcon;
        }
        catch (Exception e)
        {
            GD.Print($"[Foreve][Dual] 弹窗头像 CharacterSelectIcon 读取异常: {e.Message}");
        }

        return null;
    }
}
