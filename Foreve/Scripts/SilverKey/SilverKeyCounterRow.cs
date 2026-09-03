using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using STS2RitsuLib.Combat.SecondaryResources;

namespace Foreve.Scripts.SilverKey;

/// <summary>
/// 银钥计数器：固定素材图显示（银钥数量 N（0~15）→ 显示 silver_key_count/N.png，N=0 隐藏），
/// 位置固定在能量计数器（NEnergyCounter）上方，不再跟随玩家立绘。
/// 2026-08-13 改版：不调 base._Ready()（阻止基类创建「图标+数字」老计数器，消除新旧重复显示）；
/// 新计数改为「老银钥图标素材（silver_key_small.png）作背景底板 + 数量图（0~15）居中叠加」。
/// </summary>
public partial class SilverKeyCounterRow : NSecondaryResourceCounterRow
{
    /// <summary>银钥资源 LocalId（与 SilverKeyResource.cs 注册一致）。</summary>
    private const string SilverKeyLocalId = "silver_key";

    /// <summary>银钥达到该值后按钮显著变亮并可点击（点击消耗全部银钥并抽取钥令）。</summary>
    private const int InvokableThreshold = 5;

    /// <summary>可点击状态下的常亮倍率（显著亮于普通状态）。</summary>
    private const float InvokableBrightness = 1.32f;

    /// <summary>可点击状态下的悬停倍率（进一步提亮）。</summary>
    private const float InvokableHoverBrightness = 1.55f;

    /// <summary>背景底板素材：老银钥 UI 图标（128×128，原尺寸显示）。</summary>
    private const string BackplatePath = "res://Foreve/Assets/UI/UI/silver_key_small.png";

    /// <summary>数量图路径前缀（0.png ~ 15.png 对应银钥数量 0~15）。</summary>
    private const string IconBasePath = "res://Foreve/Assets/UI/silver_key_count/";

    /// <summary>底板显示尺寸：素材原图 128×128 原尺寸（可调）。</summary>
    private static readonly Vector2 BackplateDisplaySize = new(128f, 128f);

    /// <summary>数量图显示尺寸：素材原图 80×80 原尺寸显示（可调）。</summary>
    private static readonly Vector2 CountIconDisplaySize = new(80f, 80f);

    /// <summary>银钥图标底边与能量计数器顶边的间距（可调）。</summary>
    private const float GapAboveEnergyCounter = 12f;

    /// <summary>银钥图标相对能量计数器的 X 偏移（-20 = 左移 20px，可调）。</summary>
    private const float EnergyXOffset = -20f;

    private readonly Texture2D[] _icons = new Texture2D[16]; // 下标 0~15（数量图）

    private Texture2D? _backplate;

    private TextureRect? _backplateRect; // 背景底板（老银钥图标素材）

    private TextureRect? _countIcon; // 数量图（底板子节点，居中叠加）

    /// <summary>上次显示的数量（检测变化用；只在数量变化时播放过渡动画。-1 = 未显示/待首播）。</summary>
    private int _lastAmount = -1;

    /// <summary>当前过渡动画 Tween（新动画开始前 Kill 旧 Tween，避免连按时互相干扰）。</summary>
    private Tween? _animTween;

    /// <summary>持续呼吸动画 Tween 列表（B 方案：底板缩放/数量图缩放/透明度各一条循环；过渡动画播放期间暂停）。</summary>
    private readonly List<Tween> _breathTweens = new();

    /// <summary>节点未 Ready 前缓存的待显示数量，-1 = 无待处理刷新。</summary>
    private int _pendingAmount = -1;

    private NEnergyCounter? _energyCounter;
    private bool _energyCounterSearched;

    private Player? _followedPlayer;

    /// <summary>当前是否处于「银钥≥5，可点击抽取钥令」状态。</summary>
    private bool _isInvokable;

    /// <summary>当前鼠标是否悬停在可点击按钮上（进一步提亮反馈）。</summary>
    private bool _isHovered;

    /// <summary>防止连点/动画期间重复触发钥令抽取。</summary>
    private bool _invocationInProgress;

    public void SetFollowedPlayer(Player? player)
    {
        _followedPlayer = player;
        // optimizer：玩家绑定（战斗激活）时重定位一次，替代原每帧 _Process 轮询
        CallDeferred(nameof(Reposition));
    }

    /// <summary>探针：统计本行实例数（排查「银钥两处显示」）。</summary>
    private static int _instanceCount;

    public override void _Ready()
    {
        // 注意：不调 base._Ready() —— 基类 _Ready 会创建 _row（HBox）并在 Refresh 时为每个
        // 可见资源 GetOrCreateCounter（老银钥「图标+数字」计数器），导致新旧计数重复显示。
        // 本行完全自绘；基类 _EnterTree/_ExitTree 在 AutoRefresh=false（默认）时只调
        // SetBoundState(null)，与初始 _boundState=null ReferenceEquals 直接返回，无副作用，
        // 不会因 _row 为 null 触发 NRE。基类 Refresh 非 virtual 已被 new 隐藏，注册回调
        // （SilverKeyResource.cs RegisterCombatUi update）只调本类的 SetFollowedPlayer+Refresh。
        _instanceCount++;
        GD.Print($"[Foreve] SilverKeyRow 创建 #{_instanceCount} 父节点={GetParent()?.GetPath()}");

        // 本行自绘 128×128 图标；银钥≥5 时可点击（MouseFilter 由 ApplyAmount 动态切换）
        CustomMinimumSize = BackplateDisplaySize;
        Size = BackplateDisplaySize;
        MouseFilter = MouseFilterEnum.Ignore;
        TooltipText = "银钥能量攒满后点击：消耗全部银钥，抽取三枚钥令";

        // 预加载素材：老银钥图标（底板）+ 数量图（0~15）
        _backplate = GD.Load<Texture2D>(BackplatePath);
        for (var n = 0; n <= 15; n++)
            _icons[n] = GD.Load<Texture2D>($"{IconBasePath}{n}.png");

        // 背景底板：老银钥图标素材，128×128 原尺寸
        _backplateRect = new TextureRect
        {
            Name = "SilverKeyBackplate",
            Texture = _backplate,
            CustomMinimumSize = BackplateDisplaySize,
            Size = BackplateDisplaySize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_backplateRect);

        // 数量图：叠加在底板中央（(128-80)/2=24 偏移居中，可调）
        _countIcon = new TextureRect
        {
            Name = "SilverKeyCountIcon",
            Texture = null,
            Position = (BackplateDisplaySize - CountIconDisplaySize) / 2f,
            CustomMinimumSize = CountIconDisplaySize,
            Size = CountIconDisplaySize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _backplateRect.AddChild(_countIcon);

        // 处理 Ready 前缓存的刷新
        if (_pendingAmount >= 0)
        {
            ApplyAmount(_pendingAmount);
            _pendingAmount = -1;
        }

        // 钥令按钮交互：银钥≥5 时显著变亮；悬停进一步提亮；左键点击消耗全部银钥
        MouseEntered += () =>
        {
            _isHovered = true;
            ApplyInvokableVisual();
        };
        MouseExited += () =>
        {
            _isHovered = false;
            ApplyInvokableVisual();
        };

        // optimizer：位置改为事件驱动（战斗激活/可见性变化时重定位），替代每帧 _Process 轮询
        VisibilityChanged += OnVisibilityChanged;
        CallDeferred(nameof(Reposition));

        // B 方案：持续呼吸/脉动（先设置枢轴为各自中心，再启动循环动画）
        _backplateRect.PivotOffset = _backplateRect.Size / 2f;
        _countIcon.PivotOffset = _countIcon.Size / 2f;
        StartBreathing();
    }

    private void OnVisibilityChanged()
    {
        if (Visible) CallDeferred(nameof(Reposition));
    }

    /// <summary>
    /// 刷新显示（RegisterCombatUi 的 update 回调调用，签名与基类一致）。
    /// 注意：基类 Refresh 非 virtual，这里用 new 隐藏；本行完全自绘，
    /// 不调 base.Refresh，因此基类的「图标+数字」计数器不会被创建。
    /// </summary>
    public new void Refresh(Player? player, IReadOnlyList<SecondaryResourceDefinition> visibleDefinitions)
    {
        var amount = 0;
        if (player != null)
        {
            foreach (var def in visibleDefinitions)
            {
                if (def.LocalId != SilverKeyLocalId) continue;
                amount = SecondaryResourceCmd.Get(player, def.Id);
                break;
            }
        }

        if (!IsNodeReady())
        {
            _pendingAmount = amount;
            return;
        }
        ApplyAmount(amount);
    }

    private void ApplyAmount(int amount)
    {
        if (_backplateRect == null || _countIcon == null) return;

        // 0~15 显示对应数量图；超出素材范围（>15）隐藏
        var show = amount is >= 0 and <= 15;
        _backplateRect.Visible = show;
        if (show)
        {
            _countIcon.Texture = _icons[amount];
            // 仅在数量变化（含隐藏后重新显示）时播放过渡动画，
            // 避免 RegisterCombatUi 每次刷新都重复触发。
            if (amount != _lastAmount)
            {
                _lastAmount = amount;
                PlayCountTransition();
            }
        }
        else
        {
            _lastAmount = -1; // 隐藏后重显视为新变化 → 重新播放动画
        }
        Visible = show;

        // 银钥≥5：按钮显著变亮、可点击；否则恢复普通显示并屏蔽点击
        _isInvokable = show && amount >= InvokableThreshold;
        _isHovered = false;
        ApplyInvokableVisual();
    }

    /// <summary>
    /// 可点击状态视觉：银钥≥5 时显著变亮（1.32 倍），悬停进一步提亮（1.55 倍）；
    /// 小于 5 时恢复白色并屏蔽点击（MouseFilter.Ignore，不拦截战斗 UI）。
    /// </summary>
    private void ApplyInvokableVisual()
    {
        if (_backplateRect == null || _countIcon == null) return;

        if (!_isInvokable)
        {
            MouseFilter = MouseFilterEnum.Ignore;
            _backplateRect.SelfModulate = Colors.White;
            _countIcon.SelfModulate = Colors.White;
            return;
        }

        MouseFilter = MouseFilterEnum.Stop;
        var brightness = _isHovered ? InvokableHoverBrightness : InvokableBrightness;
        var color = new Color(brightness, brightness, brightness, 1f);
        _backplateRect.SelfModulate = color;
        _countIcon.SelfModulate = color;
    }

    /// <summary>
    /// 左键点击：银钥≥5 时进入钥令抽取流程（消耗全部银钥 → 三选一 → 执行）。
    /// 点击期间屏蔽重复触发；消耗后 Refresh 会把行隐藏（银钥归 0）。
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (!_isInvokable || _invocationInProgress) return;
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;

        _invocationInProgress = true;
        _isHovered = false;
        ApplyInvokableVisual();
        HandleInvocationAsync();
    }

    private async void HandleInvocationAsync()
    {
        try
        {
            var player = _followedPlayer;
            if (player == null)
            {
                GD.Print("[Foreve][SilverKey] 银钥按钮点击：未绑定玩家");
                return;
            }

            GD.Print($"[Foreve][SilverKey] 银钥按钮点击，当前 "
                + $"{SecondaryResourceCmd.Get(player, SilverKeyResource.ResourceId)}");
            await SilverKeyOrderManager.TryInvokeAsync(player);
        }
        catch (Exception ex)
        {
            GD.Print($"[Foreve][SilverKey] 银钥按钮点击异常: {ex}");
        }
        finally
        {
            _invocationInProgress = false;
        }
    }

    /// <summary>
    /// B 方案：持续呼吸/脉动。底板与数量图以 1.2s 周期同步缩放（1.0↔1.02 / 1.0↔1.03）
    /// 并轻微改变数量图透明度（1.0↔0.96），模拟银钥能量"呼吸"。
    /// 独立于数量过渡动画运行；PlayCountTransition 播放期间会 Pause 本动画，结束后恢复。
    /// </summary>
    private void StartBreathing()
    {
        if (_breathTweens.Count > 0) return;

        GD.Print("[Foreve] SilverKeyRow 呼吸动画启动");
        // 每条属性一条独立循环 Tween（0.6s/段 × 2 段 = 1.2s 周期，三条同时启动保持同步），
        // Godot 4 中 PropertyTweener 不支持链式 TweenProperty，串行段用「默认串行模式」自然衔接；
        // 注意：scale 属性必须传 Vector2（float Variant 不会被引擎转成 Vector2，Tween 会静默失效），
        // modulate:a 属性传 float。峰值：背板 1.03 / 数量图 1.02 / 透明度 0.96。 
        _breathTweens.Add(CreateBreathTween(_backplateRect, "scale", new Vector2(1.03f, 1.03f)));
        _breathTweens.Add(CreateBreathTween(_countIcon, "scale", new Vector2(1.02f, 1.02f)));
        _breathTweens.Add(CreateBreathTween(_countIcon, "modulate:a", 0.96f));
    }

    /// <summary>单条呼吸循环（Vector2 属性版）：值 1.0 → peak → 1.0（各 0.6s，1.2s 周期）。</summary>
    private Tween CreateBreathTween(GodotObject target, string property, Vector2 peak)
    {
        var t = CreateTween().SetLoops();
        // 默认串行模式：第一段升到峰值，第二段回落到 1.0，循环往复
        t.TweenProperty(target, property, Variant.From(peak), 0.6f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        t.TweenProperty(target, property, Variant.From(Vector2.One), 0.6f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        return t;
    }

    /// <summary>单条呼吸循环（float 属性版）：值 1.0 → peak → 1.0（各 0.6s，1.2s 周期）。</summary>
    private Tween CreateBreathTween(GodotObject target, string property, float peak)
    {
        var t = CreateTween().SetLoops();
        // 默认串行模式：第一段升到峰值，第二段回落到 1.0，循环往复
        t.TweenProperty(target, property, Variant.From(peak), 0.6f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        t.TweenProperty(target, property, Variant.From(1f), 0.6f)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        return t;
    }

    /// <summary>
    /// 数量变化过渡动画：从 50% 缩小透明，快速放大淡入，再带弹性的 squash 回落到 100%。
    /// 事件驱动、一次性播放（约 0.4s），连按时会 Kill 上一个 Tween 重新播；
    /// 播放期间暂停持续呼吸动画，结束（含被 Kill）后恢复，避免两个动画争抢 scale/alpha。
    /// </summary>
    private void PlayCountTransition()
    {
        if (_countIcon == null) return;
        _animTween?.Kill();
        foreach (var bt in _breathTweens) bt?.Pause();

        _countIcon.PivotOffset = _countIcon.Size / 2f;
        _countIcon.Scale = Vector2.One * 0.5f;
        _countIcon.Modulate = new Color(1f, 1f, 1f, 0f);

        var tween = CreateTween();
        // 第一阶段（并行）：放大到 125% + 淡入
        tween.SetParallel(true);
        tween.TweenProperty(_countIcon, "scale", Vector2.One * 1.25f, 0.10f)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_countIcon, "modulate:a", 1f, 0.12f)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.Out);
        // 第二阶段（串行）：回弹到正常尺寸
        tween.Chain()
             .TweenProperty(_countIcon, "scale", Vector2.One, 0.30f)
             .SetTrans(Tween.TransitionType.Back)
             .SetEase(Tween.EaseType.Out);
        tween.Finished += () =>
        {
            _animTween = null;
            foreach (var bt in _breathTweens) bt?.Play(); // 过渡结束（含被 Kill）后恢复呼吸
        };
        _animTween = tween;
    }

    /// <summary>
    /// 重定位：能量计数器正上方（不再跟随玩家立绘）。事件驱动调用（战斗激活/可见性变化/Ready），
    /// 替代原每帧 _Process 轮询——能量计数器在战斗中位置恒定，无需逐帧刷新。
    /// </summary>
    public void Reposition()
    {
        if (!Visible || _backplateRect == null || !_backplateRect.Visible) return;

        // 位置：能量计数器正上方（不再跟随玩家立绘）
        var energyCounter = FindEnergyCounter();
        if (energyCounter == null) return;

        var energyPos = energyCounter.GlobalPosition;
        GlobalPosition = new Vector2(
            energyPos.X + EnergyXOffset,
            energyPos.Y - BackplateDisplaySize.Y - GapAboveEnergyCounter);
    }

    /// <summary>
    /// 查找能量计数器节点。NEnergyCounter 未加入任何 group（IL 确认无 AddToGroup），
    /// 挂在 NCombatUi 的 %EnergyCounterContainer 下 → 从本行父节点（NCombatUi）递归查找类型实例。
    /// 找到后缓存；节点销毁（离树/释放）后自动失效重新查找。
    /// </summary>
    private NEnergyCounter? FindEnergyCounter()
    {
        if (!_energyCounterSearched)
        {
            _energyCounterSearched = true;
            _energyCounter = FindEnergyCounterRecursive(GetParent());
        }

        if (_energyCounter != null && !GodotObject.IsInstanceValid(_energyCounter))
        {
            _energyCounter = null;
            _energyCounterSearched = false;
        }
        return _energyCounter;
    }

    private static NEnergyCounter? FindEnergyCounterRecursive(Node? node)
    {
        if (node == null) return null;
        if (node is NEnergyCounter counter) return counter;

        foreach (var child in node.GetChildren())
        {
            var found = FindEnergyCounterRecursive(child);
            if (found != null) return found;
        }
        return null;
    }
}
