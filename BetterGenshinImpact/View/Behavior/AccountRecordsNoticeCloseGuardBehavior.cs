using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>预建关闭按钮的圆形遮罩，提示收起后保护原位置，切换提示时弃置。</summary>
public sealed class AccountRecordsNoticeCloseGuardBehavior : Behavior<ButtonBase>
{
    /// <summary>当前提示身份；关闭变为 null 时保留保护，打开另一提示时清理。</summary>
    public static readonly DependencyProperty NoticeTargetProperty = DependencyProperty.Register(
        nameof(NoticeTarget), typeof(object), typeof(AccountRecordsNoticeCloseGuardBehavior),
        new PropertyMetadata(null, OnNoticeTargetChanged));
    /// <summary>绑定当前提示行，使旧遮罩不干扰新提示。</summary>
    public object? NoticeTarget { get => GetValue(NoticeTargetProperty); set => SetValue(NoticeTargetProperty, value); }
    /// <summary>实际命中测试遮罩所在的装饰层。</summary>
    private AdornerLayer? _layer;
    /// <summary>提示以外的稳定表格宿主。</summary>
    private UserControl? _host;
    /// <summary>当前预建或已启用的遮罩。</summary>
    private AccountRecordsCloseGuardAdorner? _guard;
    /// <summary>所属窗口，负责圈外移动和失焦清理。</summary>
    private Window? _window;
    /// <summary>关闭时的指针位置，忽略布局产生的无位移事件。</summary>
    private Point _lastPointer;

    /// <summary>新提示不继承旧提示的保护区。</summary>
    private static void OnNoticeTargetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue != null) ((AccountRecordsNoticeCloseGuardBehavior)sender).Clear();
    }

    /// <summary>按下时预建，Click 命令之前启用。</summary>
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.PreviewMouseLeftButtonDown += OnPrepare;
        AssociatedObject.Click += OnCloseClick;
        AssociatedObject.IsVisibleChanged += OnButtonVisibilityChanged;
        AssociatedObject.LayoutUpdated += OnButtonLayoutUpdated;
        AssociatedObject.LostMouseCapture += OnLostCapture;
    }

    /// <summary>移除所有按钮与宿主监听。</summary>
    protected override void OnDetaching()
    {
        AssociatedObject.PreviewMouseLeftButtonDown -= OnPrepare;
        AssociatedObject.Click -= OnCloseClick;
        AssociatedObject.IsVisibleChanged -= OnButtonVisibilityChanged;
        AssociatedObject.LayoutUpdated -= OnButtonLayoutUpdated;
        AssociatedObject.LostMouseCapture -= OnLostCapture;
        Clear();
        base.OnDetaching();
    }

    /// <summary>提前建立不可命中的遮罩，原按钮仍正常接收本次按下和抬起。</summary>
    private void OnPrepare(object sender, MouseButtonEventArgs e) => Prepare();

    /// <summary>在提示仍可见时确定位置并完成装饰层布局。</summary>
    private void Prepare()
    {
        Clear();
        DependencyObject? parent = AssociatedObject;
        while (parent != null && parent is not UserControl) parent = VisualTreeHelper.GetParent(parent);
        if (parent is not UserControl host) return;
        var layer = AdornerLayer.GetAdornerLayer(host);
        var window = Window.GetWindow(host);
        if (layer == null || window == null) return;
        _host = host;
        _layer = layer;
        _window = window;
        var center = AssociatedObject.TranslatePoint(new Point(AssociatedObject.ActualWidth / 2,
            AssociatedObject.ActualHeight / 2), host);
        _guard = new AccountRecordsCloseGuardAdorner(host, center, Clear);
        layer.Add(_guard);
        layer.UpdateLayout();
        window.PreviewMouseMove += OnMouseMove;
        window.Deactivated += OnWindowDeactivated;
        host.Unloaded += OnHostUnloaded;
        host.IsVisibleChanged += OnHostVisibilityChanged;
    }

    /// <summary>原按钮 Click 先于关闭命令执行，遮罩此时立即参与实际命中测试。</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        // 只有鼠标按下预建的遮罩才参与关闭；键盘 Click 不创建鼠标保护。
        if (_guard == null || _window == null) return;
        _lastPointer = Mouse.GetPosition(_window);
        _guard.IsHitTestVisible = true;
    }

    /// <summary>取消按压时清理预建层；有效关闭后的鼠标抬起不清理。</summary>
    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        // Button 会先释放捕获再触发 Click，延至本次输入结束才能判断是否取消。
        var prepared = _guard;
        AssociatedObject.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(_guard, prepared) && _guard is { IsHitTestVisible: false }) Clear();
        }));
    }

    /// <summary>只有指针真正移出圆形区域才清理。</summary>
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_guard is not { IsHitTestVisible: true } || _window == null || _host == null) return;
        var point = e.GetPosition(_window);
        if (point == _lastPointer) return;
        _lastPointer = point;
        if (!_guard.Contains(e.GetPosition(_host))) Clear();
    }

    /// <summary>新提示重新显示关闭按钮时，旧保护不得挡住新的 X。</summary>
    private void OnButtonVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Clear();
    }

    /// <summary>可见按钮因提示内容变化而移动时弃置旧位置。</summary>
    private void OnButtonLayoutUpdated(object? sender, EventArgs e)
    {
        if (_guard == null || _host == null || !AssociatedObject.IsVisible) return;
        var center = AssociatedObject.TranslatePoint(new Point(AssociatedObject.ActualWidth / 2,
            AssociatedObject.ActualHeight / 2), _host);
        if ((center - _guard.Center).LengthSquared > 1) Clear();
    }

    /// <summary>分页或宿主卸载时清理。</summary>
    private void OnHostUnloaded(object sender, RoutedEventArgs e) => Clear();
    /// <summary>分页隐藏时清理。</summary>
    private void OnHostVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false) Clear();
    }
    /// <summary>窗口失焦时清理。</summary>
    private void OnWindowDeactivated(object? sender, EventArgs e) => Clear();

    /// <summary>从视觉树移除遮罩并解除全部临时事件。</summary>
    private void Clear()
    {
        if (_guard != null) _layer?.Remove(_guard);
        _guard = null;
        _layer = null;
        if (_host != null)
        {
            _host.Unloaded -= OnHostUnloaded;
            _host.IsVisibleChanged -= OnHostVisibilityChanged;
            _host = null;
        }
        if (_window != null)
        {
            _window.PreviewMouseMove -= OnMouseMove;
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }
    }
}
