using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;

namespace Foreve.Scripts.SilverKey;

/// <summary>
/// 钥令三选一 UI：1：2 背景板 + 居中透明底钥令图（横宽 = 背景板横宽 2/3）。
/// 悬停时选项边缘亮起；点击后在该选项周围播放发光粒子，短暂停留后返回选择结果。
/// </summary>
public static class SilverKeyOrderUi
{
    private const int CanvasLayerIndex = 180; // 高于战斗 UI，低于角色头像选择弹窗（200）

    /// <summary>背景板显示尺寸（1：2；2026-08-25 按用户要求再放大 20%：200×400 → 240×480）。</summary>
    private static readonly Vector2 BoardSize = new(240f, 480f);

    /// <summary>钥令图显示尺寸：横宽 = 背景板横宽 × 2/3（90×90 素材按此缩放）。</summary>
    private static readonly Vector2 IconSize = new(BoardSize.X * 2f / 3f, BoardSize.X * 2f / 3f);

    /// <summary>钥令图相对背景板中心的上移量（为正上方腾出效果文字区域）。</summary>
    private const float IconUpShift = 20f;

    /// <summary>背景板底部效果文字区域（大小/字号保持不变）。</summary>
    private const float DescriptionAreaHeight = 126f;

    private const float DescriptionAreaWidth = 172f;

    private const int DescriptionFontSize = 14;

    /// <summary>文字距背景板左右/下边的留白（放大背景板后文字不再贴边）。</summary>
    private static readonly float DescriptionMarginX = (BoardSize.X - DescriptionAreaWidth) / 2f;

    private const float DescriptionBottomMargin = 34f;

    private static readonly Color HoverModulate = new(1.08f, 1.08f, 1.08f, 1f);
    private static readonly Color NormalModulate = Colors.White;

    /// <summary>悬停白色模糊光圈：显示尺寸比背景板四周各多 15px/5px。</summary>
    private static readonly Vector2 HoverRingSize = BoardSize + new Vector2(30f, 10f);

    private static TaskCompletionSource<SilverKeyOrderDefinition?>? _currentChoice;
    private static CanvasLayer? _currentLayer;

    public static async Task<SilverKeyOrderDefinition?> ShowAsync(
        Player player,
        IReadOnlyList<SilverKeyOrderDefinition> options)
    {
        if (options == null || options.Count == 0) return null;

        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null)
        {
            GD.Print("[Foreve][SilverKey] 场景树不可用，钥令回退执行第一项");
            return options[0];
        }

        var layer = new CanvasLayer { Layer = CanvasLayerIndex };
        tree.Root.AddChild(layer);
        _currentLayer = layer;

        var tcs = new TaskCompletionSource<SilverKeyOrderDefinition?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _currentChoice = tcs;
        try
        {
            var dim = new ColorRect
            {
                Color = new Color(0f, 0f, 0f, 0.65f),
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(dim);

            var panel = new PanelContainer();
            panel.SetAnchorsPreset(Control.LayoutPreset.Center);
            panel.GrowHorizontal = Control.GrowDirection.Both;
            panel.GrowVertical = Control.GrowDirection.Both;
            layer.AddChild(panel);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 18);
            panel.AddChild(vbox);

            var title = new Label
            {
                Text = "选择一枚钥令",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            title.AddThemeFontSizeOverride("font_size", 30);
            vbox.AddChild(title);

            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            row.AddThemeConstantOverride("separation", 22);
            vbox.AddChild(row);

            foreach (var order in options)
                row.AddChild(BuildOption(order, tcs));

            return await tcs.Task;
        }
        finally
        {
            if (_currentChoice == tcs) _currentChoice = null;
            if (_currentLayer == layer) _currentLayer = null;
            layer.QueueFree();
        }
    }

    /// <summary>战斗结束等外部中断时强制关闭当前钥令选择（返回 null，不执行）。</summary>
    public static void ForceClose()
    {
        _currentChoice?.TrySetResult(null);
    }

    private static Control BuildOption(
        SilverKeyOrderDefinition order,
        TaskCompletionSource<SilverKeyOrderDefinition?> tcs)
    {
        var background = LoadTexture(SilverKeyOrderCatalog.BackgroundFileName);
        var icon = LoadTexture(order.IconFileName);

        var card = new PanelContainer
        {
            CustomMinimumSize = BoardSize,
            Size = BoardSize,
            ClipContents = false, // 允许白色光圈绘制到选项边框之外
            TooltipText = $"{order.Name}\n{order.Description}",
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        card.AddThemeStyleboxOverride("panel", CreateOptionStyle());

        var board = new TextureRect
        {
            Texture = background,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = BoardSize,
            Size = BoardSize,
            ClipContents = false, // 光圈需要绘制到背景板之外
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        card.AddChild(board);

        // 悬停光圈：纯白、模糊、有渐变厚度的圆角矩形光环，平时隐藏，悬停时显示
        var hoverRing = new TextureRect
        {
            Texture = GetOrCreateHoverRingTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Size = HoverRingSize,
            Position = (BoardSize - HoverRingSize) / 2f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };

        if (icon != null)
        {
            var iconRect = new TextureRect
            {
                Texture = icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = IconSize,
                Size = IconSize,
                // 相对背景板中心稍微上移，为底部效果文字让出空间
                Position = (BoardSize - IconSize) / 2f - new Vector2(0f, IconUpShift),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            board.AddChild(iconRect);
        }

        // 钥令效果文字：放在背景板下方区域（钥令图下方），文字大小不变，四周留白加大
        var description = new Label
        {
            Text = order.Description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(DescriptionAreaWidth, DescriptionAreaHeight),
            Size = new Vector2(DescriptionAreaWidth, DescriptionAreaHeight),
            Position = new Vector2(
                DescriptionMarginX,
                BoardSize.Y - DescriptionAreaHeight - DescriptionBottomMargin),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        description.AddThemeFontSizeOverride("font_size", DescriptionFontSize);
        description.AddThemeColorOverride("font_color", Colors.White);
        description.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        description.AddThemeConstantOverride("outline_size", 4);
        board.AddChild(description);

        // 光圈作为 board 的子节点（TextureRect 不会像 PanelContainer 那样强制布局子节点），
        // 并在最后加入，保证绘制在背景板与文字之上。
        board.AddChild(hoverRing);

        card.MouseEntered += () =>
        {
            if (!GodotObject.IsInstanceValid(card)) return;
            card.SelfModulate = HoverModulate;
            if (GodotObject.IsInstanceValid(hoverRing)) hoverRing.Visible = true;
        };

        card.MouseExited += () =>
        {
            if (!GodotObject.IsInstanceValid(card)) return;
            card.SelfModulate = NormalModulate;
            if (GodotObject.IsInstanceValid(hoverRing)) hoverRing.Visible = false;
        };

        card.GuiInput += async inputEvent =>
        {
            if (tcs.Task.IsCompleted) return;
            if (inputEvent is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;

            // 点击后先在该钥令四周播放发光粒子，其他选项不再响应
            PlaySelectionBurst(card);
            foreach (var sibling in card.GetParent()?.GetChildren() ?? new Godot.Collections.Array<Node>())
            {
                if (sibling is Control c) c.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
            card.MouseFilter = Control.MouseFilterEnum.Ignore;

            // 让发光粒子播完（约 0.75s）再返回结果并关闭弹窗
            await Task.Delay(750);
            tcs.TrySetResult(order);
        };

        return card;
    }

    private static StyleBoxFlat CreateOptionStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.03f, 0.05f, 0.92f),
            BorderColor = new Color(1f, 1f, 1f, 0f),
            BorderWidthLeft = 0,
            BorderWidthTop = 0,
            BorderWidthRight = 0,
            BorderWidthBottom = 0,
        };
        var corner = 12;
        style.CornerRadiusTopLeft = corner;
        style.CornerRadiusTopRight = corner;
        style.CornerRadiusBottomLeft = corner;
        style.CornerRadiusBottomRight = corner;
        style.ContentMarginLeft = 0f;
        style.ContentMarginTop = 0f;
        style.ContentMarginRight = 0f;
        style.ContentMarginBottom = 0f;
        return style;
    }

    // ── 悬停白色模糊光圈 ────────────────────────────────────────────────

    private static Texture2D? _hoverRingTexture;

    /// <summary>
    /// 程序化生成纯白、模糊、有渐变厚度的圆角矩形光圈：
    /// 以背景板外边缘为中心做高斯式亮度衰减，越靠近边缘越亮，内外两侧逐渐透明。
    /// 纹理像素尺寸与 HoverRingSize 等比，拉伸到显示尺寸后模糊厚度均匀。
    /// </summary>
    private static Texture2D? GetOrCreateHoverRingTexture()
    {
        if (_hoverRingTexture != null && GodotObject.IsInstanceValid(_hoverRingTexture))
            return _hoverRingTexture;

        try
        {
            // 与 HoverRingSize(304×544) 等比缩半，降低生成开销
            const int width = 152;
            const int height = 272;

            var halfWidth = BoardSize.X / (2f * HoverRingSize.X);
            var halfHeight = BoardSize.Y / (2f * HoverRingSize.Y);
            var cornerRadius = 14f / HoverRingSize.X;
            var sigma = 9f / HoverRingSize.X; // 约 9px 高斯模糊，形成渐变厚度

            var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
            for (var y = 0; y < height; y++)
            {
                var py = (y + 0.5f) / height - 0.5f;
                for (var x = 0; x < width; x++)
                {
                    var px = (x + 0.5f) / width - 0.5f;
                    var distance = RoundedRectSdf(px, py, halfWidth, halfHeight, cornerRadius);
                    var alpha = MathF.Exp(-(distance * distance) / (2f * sigma * sigma));
                    image.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp(alpha, 0f, 1f) * 0.92f));
                }
            }

            _hoverRingTexture = ImageTexture.CreateFromImage(image);
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][SilverKey] 白色光圈纹理生成失败: {ex.Message}");
        }
        return _hoverRingTexture;
    }

    /// <summary>圆角矩形有符号距离：负值在矩形内部，0 在边缘，正值在外部。</summary>
    private static float RoundedRectSdf(float px, float py, float halfWidth, float halfHeight, float radius)
    {
        var qx = Math.Abs(px) - (halfWidth - radius);
        var qy = Math.Abs(py) - (halfHeight - radius);
        var outside = MathF.Sqrt(MathF.Pow(Math.Max(qx, 0f), 2f) + MathF.Pow(Math.Max(qy, 0f), 2f)) - radius;
        var inside = Math.Min(Math.Max(qx, qy), 0f);
        return outside + inside;
    }

    /// <summary>选中粒子：白色光点从背景板四周边线向外轻微散发后淡出。</summary>
    private static void PlaySelectionBurst(Control option)
    {
        try
        {
            const int pointsPerEdge = 7;
            var halfWidth = BoardSize.X / 2f;
            var halfHeight = BoardSize.Y / 2f;

            var emissionPoints = new List<Vector2>(pointsPerEdge * 4);
            var emissionNormals = new List<Vector2>(pointsPerEdge * 4);

            for (var i = 0; i < pointsPerEdge; i++)
            {
                var t = (i + 0.5f) / pointsPerEdge;
                var x = Mathf.Lerp(-halfWidth, halfWidth, t);

                emissionPoints.Add(new Vector2(x, -halfHeight)); // 上边
                emissionNormals.Add(Vector2.Up);
                emissionPoints.Add(new Vector2(x, halfHeight)); // 下边
                emissionNormals.Add(Vector2.Down);
                emissionPoints.Add(new Vector2(-halfWidth, Mathf.Lerp(-halfHeight, halfHeight, t))); // 左边
                emissionNormals.Add(Vector2.Left);
                emissionPoints.Add(new Vector2(halfWidth, Mathf.Lerp(-halfHeight, halfHeight, t))); // 右边
                emissionNormals.Add(Vector2.Right);
            }

            var particles = new CpuParticles2D
            {
                Name = "SilverKeySelectionParticles",
                Emitting = true,
                Amount = emissionPoints.Count * 3,
                Lifetime = 0.7,
                OneShot = true,
                Explosiveness = 0.9f,
                EmissionShape = CpuParticles2D.EmissionShapeEnum.DirectedPoints,
                EmissionPoints = emissionPoints.ToArray(),
                EmissionNormals = emissionNormals.ToArray(),
                Direction = Vector2.Up,
                Spread = 22f,
                Gravity = Vector2.Zero,
                InitialVelocityMin = 24f,
                InitialVelocityMax = 62f,
                ScaleAmountMin = 1.6f,
                ScaleAmountMax = 4.2f,
                Color = new Color(1f, 1f, 1f, 0.95f),
                Position = option.Size / 2f,
                ZIndex = 20,
            };
            option.AddChild(particles);
            particles.Finished += () =>
            {
                if (GodotObject.IsInstanceValid(particles)) particles.QueueFree();
            };
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][SilverKey] 钥令粒子创建失败: {ex.Message}");
        }
    }

    private static Texture2D? LoadTexture(string fileName)
    {
        // 1) PCK 内打包路径（后续把素材导入 Foreve/Assets/UI/KeyOrders 后可用）
        var resPath = $"res://Foreve/Assets/UI/KeyOrders/{fileName}";
        try
        {
            if (ResourceLoader.Exists(resPath))
                return GD.Load<Texture2D>(resPath);
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][SilverKey] res:// 纹理加载失败 {fileName}: {ex.Message}");
        }

        // 2) 兼容旧的本地素材源目录（公开版 SourceDirectory 为空，通常走第 3 步）
        var sourcePath = Path.Combine(SilverKeyOrderCatalog.SourceDirectory, fileName);
        var texture = LoadAbsoluteTexture(sourcePath);
        if (texture != null) return texture;

        // 3) mod 部署目录兜底（构建时 CopyMod 会把素材复制到 mods\Foreve\Assets\KeyOrders）
        try
        {
            var dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrWhiteSpace(dllDir))
            {
                texture = LoadAbsoluteTexture(Path.Combine(dllDir, "Assets", "KeyOrders", fileName));
                if (texture != null) return texture;
            }
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][SilverKey] mod 目录纹理加载失败 {fileName}: {ex.Message}");
        }

        GD.Print($"[Foreve][SilverKey] 钥令纹理缺失: {fileName}");
        return null;
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
            GD.Print($"[Foreve][SilverKey] 绝对路径纹理加载失败 {path}: {ex.Message}");
            return null;
        }
    }
}
