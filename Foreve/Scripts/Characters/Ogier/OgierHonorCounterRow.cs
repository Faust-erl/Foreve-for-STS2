using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using STS2RitsuLib.Combat.SecondaryResources;

namespace Foreve.Scripts.Characters.Ogier;

/// <summary>
/// 荣誉计数器行：与能力（power）图标同一行显示（血条下方），
/// 图标右下角显示当前荣誉数（不显示上限），悬停图标时在描述中显示实时上限。
/// </summary>
public partial class OgierHonorCounterRow : NSecondaryResourceCounterRow
{
    /// <summary>荣誉图标与最后一个能力图标之间的水平间距。</summary>
    private const float PowerGap = 8f;

    /// <summary>
    /// 荣誉计数样式：尺寸对齐 power 图标（48×40，图标 40×40，数字 18 号右下角），
    /// 计数只显示当前数量（FormatAmount 忽略上限）。
    /// </summary>
    private static readonly SecondaryResourceCounterStyle HonorStyle = new()
    {
        CounterSize = new Vector2(48f, 40f),
        IconSize = new Vector2(40f, 40f),
        FontSize = 18,
        OutlineSize = 8,
        // 数量标签中心相对居中图标矩形的偏移 → 数字落在图标右下角（power 风格）
        AmountLabelOffset = new Vector2(12f, 8f),
        RowSeparation = 8,
        // 页面中不显示上限（默认格式是 "当前/上限"，这里只保留当前值）
        FormatAmount = static (amount, _) => amount.ToString(),
    };

    private Player? _followedPlayer;
    private NPowerContainer? _powerContainer;
    private bool _powerContainerSearched;

    public void SetFollowedPlayer(Player? player)
    {
        _followedPlayer = player;
        // optimizer：玩家绑定（战斗激活）时重定位一次，替代原每帧 _Process 轮询
        CallDeferred(nameof(Reposition));
    }

    public override void _Ready()
    {
        GD.Print($"[Foreve] HonorRow 创建 父节点={GetParent()?.GetPath()} 父父={GetParent()?.GetParent()?.GetPath()}");
        // 先应用样式，再让基类建行容器；之后基类 Refresh 创建计数器时即用该样式
        Configure(HonorStyle);
        base._Ready();
        // optimizer：位置改为事件驱动——能力图标增删（ChildEnteredTree/ExitingTree）、
        // 能力容器位移（SetNotifyTransform 探针）、可见性变化、战斗激活时重定位；
        // 不再每帧轮询。
        VisibilityChanged += OnVisibilityChanged;
        CallDeferred(nameof(Reposition));
    }

    private void OnVisibilityChanged()
    {
        if (Visible) CallDeferred(nameof(Reposition));
    }

    /// <summary>
    /// 重定位到该玩家立绘下能力行的右缘。事件驱动调用（战斗激活/能力增删/位移/可见性变化），
    /// 替代原每帧 _Process 轮询（遍历 CreatureNodes + 逐图标 GetGlobalRect + 每帧写 GlobalPosition）。
    /// </summary>
    public void Reposition()
    {
        if (_followedPlayer == null || !Visible) return;

        var room = NCombatRoom.Instance;
        if (room == null) return;

        foreach (var creature in room.CreatureNodes)
        {
            if (!ReferenceEquals(creature.Entity, _followedPlayer.Creature)) continue;

            // 能力行 = 该玩家立绘节点下的 NPowerContainer（每场战斗重建，失效后重新查找）
            var powerContainer = FindPowerContainer(creature);
            if (powerContainer == null) return;
            AttachPowerContainerWatchers(powerContainer);

            var target = powerContainer.GlobalPosition;
            // X：跟随该行最后一个能力图标的右缘（用全局矩形，免疫缩放）；没有能力时贴行首
            var rowRight = float.NegativeInfinity;
            foreach (var child in powerContainer.GetChildren())
            {
                if (child is not Control control) continue;
                rowRight = Math.Max(rowRight, control.GetGlobalRect().End.X);
            }
            if (!float.IsNegativeInfinity(rowRight))
                target.X += rowRight - powerContainer.GlobalPosition.X + PowerGap;

            // Y：与能力行同排（顶部对齐）
            GlobalPosition = target;
            break;
        }
    }

    private NPowerContainer? _notifiedContainer;
    private TransformProbe? _transformProbe;

    /// <summary>监听能力容器：图标增删（ChildEnteredTree/ExitingTree）与整体位移（探针节点）。</summary>
    private void AttachPowerContainerWatchers(NPowerContainer container)
    {
        if (ReferenceEquals(_notifiedContainer, container)) return;
        if (_notifiedContainer != null && GodotObject.IsInstanceValid(_notifiedContainer))
        {
            _notifiedContainer.ChildEnteredTree -= OnPowerChildChanged;
            _notifiedContainer.ChildExitingTree -= OnPowerChildChanged;
        }
        _notifiedContainer = container;
        container.ChildEnteredTree += OnPowerChildChanged;
        container.ChildExitingTree += OnPowerChildChanged;
        EnsureTransformProbe(container);
    }

    /// <summary>探针节点：挂在能力容器下，容器或其祖先位移时触发 NOTIFICATION_TRANSFORM_CHANGED，跟随重定位。</summary>
    private void EnsureTransformProbe(NPowerContainer container)
    {
        if (_transformProbe != null && GodotObject.IsInstanceValid(_transformProbe)) return;
        _transformProbe = new TransformProbe();
        _transformProbe.OnTransformChanged += () => Reposition();
        container.AddChild(_transformProbe);
        _transformProbe.SetNotifyTransform(true);
    }

    private void OnPowerChildChanged(Node _)
    {
        // 图标刚增删时布局可能未完成，推迟到本帧结束再重定位
        CallDeferred(nameof(Reposition));
    }

    /// <summary>
    /// 变换探针（Node2D，无渲染）：SetNotifyTransform 开启后，自身/祖先的全局变换变化
    /// 会收到 NOTIFICATION_TRANSFORM_CHANGED——能力容器随立绘位移时据此重定位荣誉行。
    /// 探针不是 Control，会被 Reposition 的图标遍历自动排除。
    /// </summary>
    private sealed partial class TransformProbe : Node2D
    {
        public Action? OnTransformChanged;

        public override void _Notification(int what)
        {
            if (what == NotificationTransformChanged)
                OnTransformChanged?.Invoke();
        }
    }

    private NPowerContainer? FindPowerContainer(Node root)
    {
        if (!_powerContainerSearched)
        {
            _powerContainerSearched = true;
            _powerContainer = FindPowerContainerRecursive(root);
        }
        // 节点销毁（每场战斗重建）后重新查找
        if (_powerContainer != null && !GodotObject.IsInstanceValid(_powerContainer))
        {
            _powerContainer = null;
            _powerContainerSearched = false;
        }
        return _powerContainer;
    }

    private static NPowerContainer? FindPowerContainerRecursive(Node? node)
    {
        if (node == null) return null;
        if (node is NPowerContainer container) return container;
        foreach (var child in node.GetChildren())
        {
            var found = FindPowerContainerRecursive(child);
            if (found != null) return found;
        }
        return null;
    }
}
