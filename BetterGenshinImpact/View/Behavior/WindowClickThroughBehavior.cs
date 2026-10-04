using System;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Xaml.Behaviors;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 根据绑定值切换分层窗口的点击穿透。
/// 穿透时加上 WS_EX_TRANSPARENT | WS_EX_LAYERED，关闭穿透时去掉这两个样式（与原 MaskWindow.SetClickThrough 一致）。
/// </summary>
public sealed class WindowClickThroughBehavior : Behavior<Window>
{
    public static readonly DependencyProperty IsClickThroughProperty =
        DependencyProperty.Register(
            nameof(IsClickThrough),
            typeof(bool),
            typeof(WindowClickThroughBehavior),
            new PropertyMetadata(true, OnIsClickThroughChanged));

    public bool IsClickThrough
    {
        get => (bool)GetValue(IsClickThroughProperty);
        set => SetValue(IsClickThroughProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.SourceInitialized += OnWindowReady;
        AssociatedObject.Loaded += OnWindowReady;
        Apply();
    }

    protected override void OnDetaching()
    {
        AssociatedObject.SourceInitialized -= OnWindowReady;
        AssociatedObject.Loaded -= OnWindowReady;
        base.OnDetaching();
    }

    private static void OnIsClickThroughChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((WindowClickThroughBehavior)d).Apply();
    }

    private void OnWindowReady(object? sender, EventArgs e)
    {
        Apply();
    }

    private void Apply()
    {
        if (AssociatedObject == null)
        {
            return;
        }

        var hWnd = new WindowInteropHelper(AssociatedObject).Handle;
        if (hWnd == IntPtr.Zero)
        {
            return;
        }

        const int flags = (int)(User32.WindowStylesEx.WS_EX_TRANSPARENT | User32.WindowStylesEx.WS_EX_LAYERED);
        var style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
        var newStyle = IsClickThrough ? style | flags : style & ~flags;
        if (newStyle != style)
        {
            _ = User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, newStyle);
        }
    }
}
