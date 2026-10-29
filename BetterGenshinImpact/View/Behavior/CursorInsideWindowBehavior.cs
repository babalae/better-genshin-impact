using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Xaml.Behaviors;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Behavior;

/// <summary>
/// 鼠标穿透的窗口收不到鼠标事件，启用时轮询光标位置，报告可见的鼠标指针是否位于窗口上。
/// 游戏隐藏指针（例如原神三维视角）时视为不在窗口上，避免被锁在屏幕中心的指针误触发
/// </summary>
public sealed class CursorInsideWindowBehavior : Behavior<Window>
{
    /// <summary>
    /// 轮询间隔
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// 是否启用轮询
    /// </summary>
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(CursorInsideWindowBehavior),
            new PropertyMetadata(false, OnIsActiveChanged));

    /// <summary>
    /// 可见的鼠标指针是否位于窗口上（输出）
    /// </summary>
    public static readonly DependencyProperty IsCursorInsideProperty =
        DependencyProperty.Register(nameof(IsCursorInside), typeof(bool), typeof(CursorInsideWindowBehavior),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// 轮询定时器
    /// </summary>
    private DispatcherTimer? _timer;

    /// <summary>
    /// 是否启用轮询
    /// </summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>
    /// 可见的鼠标指针是否位于窗口上
    /// </summary>
    public bool IsCursorInside
    {
        get => (bool)GetValue(IsCursorInsideProperty);
        set => SetValue(IsCursorInsideProperty, value);
    }

    /// <inheritdoc />
    protected override void OnAttached()
    {
        base.OnAttached();
        _timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, OnTick, AssociatedObject.Dispatcher);
        _timer.Stop();
        UpdateTimer();
    }

    /// <inheritdoc />
    protected override void OnDetaching()
    {
        _timer?.Stop();
        _timer = null;
        base.OnDetaching();
    }

    /// <summary>
    /// 启用状态变化时启停轮询
    /// </summary>
    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((CursorInsideWindowBehavior)d).UpdateTimer();
    }

    /// <summary>
    /// 按启用状态启停轮询，停止时报告不在窗口上
    /// </summary>
    private void UpdateTimer()
    {
        if (_timer is null)
        {
            return;
        }

        if (IsActive)
        {
            _timer.Start();
            return;
        }

        _timer.Stop();
        if (IsCursorInside)
        {
            IsCursorInside = false;
        }
    }

    /// <summary>
    /// 检查光标是否可见且位于窗口矩形内
    /// </summary>
    private void OnTick(object? sender, EventArgs e)
    {
        var inside = false;
        var hWnd = AssociatedObject is null ? IntPtr.Zero : new WindowInteropHelper(AssociatedObject).Handle;
        if (hWnd != IntPtr.Zero && AssociatedObject!.IsVisible && User32.GetWindowRect(hWnd, out var rect))
        {
            var info = new User32.CURSORINFO { cbSize = (uint)Marshal.SizeOf<User32.CURSORINFO>() };
            if (User32.GetCursorInfo(ref info) && info.flags.HasFlag(User32.CursorState.CURSOR_SHOWING))
            {
                var point = info.ptScreenPos;
                inside = point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;
            }
        }

        if (inside != IsCursorInside)
        {
            IsCursorInside = inside;
        }
    }
}
