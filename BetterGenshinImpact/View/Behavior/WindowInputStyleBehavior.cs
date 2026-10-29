using System;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Xaml.Behaviors;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 按绑定值切换窗口的输入相关扩展样式：鼠标穿透（WS_EX_TRANSPARENT）、不获得焦点（WS_EX_NOACTIVATE）、
/// 不出现在 Alt+Tab（WS_EX_TOOLWINDOW）。
/// 用于 AllowsTransparency 的分层窗口：不改动 WS_EX_LAYERED，避免分层窗口失去绘制
/// </summary>
public sealed class WindowInputStyleBehavior : Behavior<Window>
{
    /// <summary>
    /// 是否鼠标穿透
    /// </summary>
    public static readonly DependencyProperty IsClickThroughProperty =
        DependencyProperty.Register(nameof(IsClickThrough), typeof(bool), typeof(WindowInputStyleBehavior),
            new PropertyMetadata(false, OnStyleChanged));

    /// <summary>
    /// 是否允许获得焦点
    /// </summary>
    public static readonly DependencyProperty IsActivatableProperty =
        DependencyProperty.Register(nameof(IsActivatable), typeof(bool), typeof(WindowInputStyleBehavior),
            new PropertyMetadata(false, OnStyleChanged));

    /// <summary>
    /// 是否不出现在 Alt+Tab 和任务栏
    /// </summary>
    public static readonly DependencyProperty HideFromAltTabProperty =
        DependencyProperty.Register(nameof(HideFromAltTab), typeof(bool), typeof(WindowInputStyleBehavior),
            new PropertyMetadata(true, OnStyleChanged));

    /// <summary>
    /// 是否鼠标穿透
    /// </summary>
    public bool IsClickThrough
    {
        get => (bool)GetValue(IsClickThroughProperty);
        set => SetValue(IsClickThroughProperty, value);
    }

    /// <summary>
    /// 是否允许获得焦点
    /// </summary>
    public bool IsActivatable
    {
        get => (bool)GetValue(IsActivatableProperty);
        set => SetValue(IsActivatableProperty, value);
    }

    /// <summary>
    /// 是否不出现在 Alt+Tab 和任务栏
    /// </summary>
    public bool HideFromAltTab
    {
        get => (bool)GetValue(HideFromAltTabProperty);
        set => SetValue(HideFromAltTabProperty, value);
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.SourceInitialized += OnWindowReady;
        AssociatedObject.Loaded += OnWindowReady;
        Apply();
    }

    /// <inheritdoc />
    protected override void OnDetaching()
    {
        AssociatedObject.SourceInitialized -= OnWindowReady;
        AssociatedObject.Loaded -= OnWindowReady;
        base.OnDetaching();
    }

    /// <summary>
    /// 任一属性变化时重新应用样式
    /// </summary>
    private static void OnStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((WindowInputStyleBehavior)d).Apply();
    }

    /// <summary>
    /// 窗口句柄创建或加载后应用样式
    /// </summary>
    private void OnWindowReady(object? sender, EventArgs e)
    {
        Apply();
    }

    /// <summary>
    /// 按当前属性设置扩展样式
    /// </summary>
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

        var style = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
        var newStyle = Set(style, (int)User32.WindowStylesEx.WS_EX_TRANSPARENT, IsClickThrough);
        newStyle = Set(newStyle, (int)User32.WindowStylesEx.WS_EX_NOACTIVATE, !IsActivatable);
        newStyle = Set(newStyle, (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW, HideFromAltTab);
        if (newStyle != style)
        {
            _ = User32.SetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE, newStyle);
        }
    }

    /// <summary>
    /// 设置或清除样式位
    /// </summary>
    private static int Set(int style, int flag, bool enabled) => enabled ? style | flag : style & ~flag;
}
