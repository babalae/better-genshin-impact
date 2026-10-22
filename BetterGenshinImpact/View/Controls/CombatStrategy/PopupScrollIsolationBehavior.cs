using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;

namespace BetterGenshinImpact.View.Controls.CombatStrategy;

/// <summary>弹出层打开期间隔离宿主滚轮，并管理弹出尺寸与键盘焦点。</summary>
public sealed class PopupScrollIsolationBehavior : Behavior<Popup>
{
    /// <summary>标准弹层宽度，供 XAML 和工作区约束共用。</summary>
    public const double DefaultPopupWidth = 640;
    /// <summary>标准弹层高度，供 XAML 和工作区约束共用。</summary>
    public const double DefaultPopupHeight = 520;
    /// <summary>等待布局完成后设置焦点的操作，关闭及卸载时取消。</summary>
    private DispatcherOperation? _focusOperation;
    /// <summary>当前宿主窗口，仅在弹出期间挂接滚轮。</summary>
    private Window? _window;
    /// <summary>挂接弹出生命周期。</summary>
    protected override void OnAttached()
    {
        AssociatedObject.Opened += OnOpened;
        AssociatedObject.Closed += OnClosed;
    }
    /// <summary>释放所有事件订阅。</summary>
    protected override void OnDetaching()
    {
        _focusOperation?.Abort();
        UnhookWindow();
        AssociatedObject.Opened -= OnOpened;
        AssociatedObject.Closed -= OnClosed;
    }
    /// <summary>按所在屏幕工作区限制面板，并把焦点交给右侧搜索或范围列表。</summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        UnhookWindow();
        _window = Window.GetWindow(AssociatedObject.PlacementTarget);
        if (_window != null)
        {
            _window.PreviewMouseWheel += OnWindowMouseWheel;
            var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(_window).Handle).WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(_window);
            if (AssociatedObject.Child is FrameworkElement child)
            {
                child.Width = Math.Max(1, Math.Min(DefaultPopupWidth, area.Width / dpi.DpiScaleX - 16));
                child.Height = Math.Max(1, Math.Min(DefaultPopupHeight, area.Height / dpi.DpiScaleY - 16));
            }
        }
        _focusOperation?.Abort();
        var popup = AssociatedObject;
        _focusOperation = popup.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (AssociatedObject != popup || !popup.IsOpen) return;
            var root = popup.Child;
            FrameworkElement? target = FindNamed<TextBox>(root, "SearchInput")
                ?? (FrameworkElement?)FindNamed<ListBox>(root, "ScopeList");
            if (target == null) target = FindNamed<ContentControl>(root, "CategoryContent");
            target?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            if (target is TextBox or ListBox) Keyboard.Focus(target);
        }));
    }
    /// <summary>关闭后恢复触发区焦点。</summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        _focusOperation?.Abort();
        var restoreFocus = _window?.IsActive == true;
        UnhookWindow();
        if (restoreFocus && AssociatedObject.PlacementTarget is FrameworkElement { IsLoaded: true, IsEnabled: true } trigger)
            Keyboard.Focus(trigger);
    }
    /// <summary>弹出区域内的滚轮由自身列表处理；阻止宿主页面收到外部滚轮。</summary>
    private void OnWindowMouseWheel(object sender, MouseWheelEventArgs e) => e.Handled = true;
    /// <summary>成对释放宿主窗口事件。</summary>
    private void UnhookWindow()
    {
        if (_window != null) _window.PreviewMouseWheel -= OnWindowMouseWheel;
        _window = null;
    }
    /// <summary>在模板生成的可视树中查找具名控件。</summary>
    internal static T? FindNamed<T>(DependencyObject? root, string name) where T : FrameworkElement
    {
        if (root == null) return null;
        if (root is T element && element.Name == name) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindNamed<T>(VisualTreeHelper.GetChild(root, i), name) is { } match) return match;
        return null;
    }
}
