using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.Infrastructure.WebView;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.View.Controls.Guide;
using BetterGenshinImpact.ViewModel.Windows;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 悬浮小窗：透明置顶、默认不获得焦点的网页窗口（见 Docs/design/guide-window.md 第 6 节）。
/// 显示、穿透、焦点等状态由 <see cref="IGuideWindowService"/> 决定，这里只负责窗口交互：拖动、缩放、圆角裁剪和 Win32 定位
/// </summary>
public partial class GuideWindow : Window, IGuideWindowView
{
    /// <summary>圆角半径（DIP）</summary>
    private const double CornerRadius = 8;

    /// <summary>
    /// 置顶层的 HWND_TOPMOST
    /// </summary>
    private static readonly HWND HwndTopmost = new(-1);

    /// <summary>
    /// 网页播放桥
    /// </summary>
    private readonly GuidePlayerBridge _bridge;

    /// <summary>
    /// 由服务关闭时允许关闭，其他关闭请求（例如交互模式下的 Alt+F4）改为隐藏
    /// </summary>
    private bool _allowClose;

    /// <summary>
    /// 缩放开始时的窗口矩形（物理像素）
    /// </summary>
    private RECT _resizeStartBounds;

    /// <summary>
    /// 缩放开始时的光标位置（物理像素）
    /// </summary>
    private POINT _resizeStartCursor;

    /// <summary>
    /// 当前缩放的边或角：L、R、T、B、TL、TR、BL、BR
    /// </summary>
    private string _resizeEdge = string.Empty;

    /// <param name="viewModel">小窗 ViewModel</param>
    /// <param name="environmentProvider">WebView2 环境入口，网页初始化时使用</param>
    /// <param name="bridgeLogger">播放桥日志</param>
    public GuideWindow(GuideWindowViewModel viewModel, IWebViewEnvironmentProvider environmentProvider,
        ILogger<GuidePlayerBridge> bridgeLogger)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        _bridge = new GuidePlayerBridge(WebView, environmentProvider, bridgeLogger);
        Loaded += (_, _) => _ = _bridge.EnsureInitializedAsync();
        SizeChanged += OnSizeChanged;
        Activated += (_, _) => ViewModel.OnWindowActivated();
        Deactivated += (_, _) => ViewModel.OnWindowDeactivated();
    }

    /// <summary>
    /// 小窗 ViewModel
    /// </summary>
    public GuideWindowViewModel ViewModel { get; }

    #region IGuideWindowView

    /// <inheritdoc />
    public IGuidePlayer Player => _bridge;

    /// <inheritdoc />
    public nint Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <inheritdoc />
    public bool IsShown => IsVisible;

    /// <inheritdoc />
    public void ShowWithoutActivation()
    {
        if (!IsVisible)
        {
            // ShowActivated 为 false：以 SW_SHOWNA 显示，不抢游戏焦点
            Show();
        }
    }

    /// <inheritdoc />
    public void HideWindow()
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    /// <inheritdoc />
    public void CloseWindow()
    {
        _allowClose = true;
        Close();
    }

    /// <inheritdoc />
    public void SetBounds(RECT bounds)
    {
        _ = User32.SetWindowPos(Handle, HWND.NULL, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            User32.SetWindowPosFlags.SWP_NOZORDER | User32.SetWindowPosFlags.SWP_NOACTIVATE
                                                   | User32.SetWindowPosFlags.SWP_NOOWNERZORDER);
    }

    /// <inheritdoc />
    public bool TryGetBounds(out RECT bounds)
    {
        bounds = default;
        var handle = new WindowInteropHelper(this).Handle;
        return handle != IntPtr.Zero && User32.GetWindowRect(handle, out bounds) && bounds.Width > 0;
    }

    /// <inheritdoc />
    public void BringToTopmost()
    {
        if (!Topmost)
        {
            return;
        }

        _ = User32.SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0,
            User32.SetWindowPosFlags.SWP_NOMOVE | User32.SetWindowPosFlags.SWP_NOSIZE
                                                | User32.SetWindowPosFlags.SWP_NOACTIVATE);
    }

    /// <inheritdoc />
    public void ActivateForInput()
    {
        // 交互模式下扩展样式已去掉 WS_EX_NOACTIVATE；快捷键或鼠标点击让本进程拥有设置前台的权限
        Activate();
        _ = User32.SetForegroundWindow(Handle);
        if (!ViewModel.IsEditingAddress)
        {
            WebView.Focus();
        }
    }

    #endregion

    #region 拖动、地址框

    /// <summary>
    /// 拖动把手：移动小窗
    /// </summary>
    private void OnDragHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        DragAndCommit();
    }

    /// <summary>
    /// 窗口栏空白处：移动小窗
    /// </summary>
    private void OnBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        e.Handled = true;
        DragAndCommit();
    }

    /// <summary>
    /// 标题：按住移动小窗；没有移动时视为点击，开始编辑地址
    /// </summary>
    private void OnTitleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!DragAndCommit() && e.ClickCount == 1)
        {
            ViewModel.BeginEditAddressCommand.Execute(null);
        }
    }

    /// <summary>
    /// 用系统拖动移动窗口；移动了位置时保存
    /// </summary>
    /// <returns>位置是否改变</returns>
    private bool DragAndCommit()
    {
        TryGetBounds(out var before);
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 鼠标已松开时 DragMove 会抛出异常
            return false;
        }

        if (!TryGetBounds(out var after) || after.Left == before.Left && after.Top == before.Top)
        {
            return false;
        }

        ViewModel.CommitBounds();
        return true;
    }

    /// <summary>
    /// 地址框显示时获得焦点并全选
    /// </summary>
    private void OnAddressBoxIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                AddressBox.Focus();
                Keyboard.Focus(AddressBox);
                AddressBox.SelectAll();
            }));
        }
    }

    /// <summary>
    /// 地址框失去焦点：取消编辑
    /// </summary>
    private void OnAddressBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ViewModel.IsEditingAddress)
        {
            ViewModel.CancelEditAddressCommand.Execute(null);
        }
    }

    #endregion

    #region 滚轮

    /// <summary>
    /// 窗口栏空白处滚轮：调整不透明度
    /// </summary>
    private void OnBarMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        e.Handled = true;
        ViewModel.OnBarWheel(e.Delta);
    }

    /// <summary>
    /// 倍速按钮滚轮：调整倍速
    /// </summary>
    private void OnRateMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        ViewModel.OnRateWheel(e.Delta);
    }

    /// <summary>
    /// 音量按钮滚轮：调整音量
    /// </summary>
    private void OnVolumeMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        ViewModel.OnVolumeWheel(e.Delta);
    }

    #endregion

    #region 缩放

    /// <summary>
    /// 开始缩放：记录窗口矩形与光标位置（物理像素）
    /// </summary>
    private void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        _resizeEdge = (sender as FrameworkElement)?.Tag as string ?? string.Empty;
        TryGetBounds(out _resizeStartBounds);
        User32.GetCursorPos(out _resizeStartCursor);
    }

    /// <summary>
    /// 缩放中：按光标位移计算新矩形，限制最小尺寸；沉浸模式下保持视频比例
    /// </summary>
    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (_resizeEdge.Length == 0 || _resizeStartBounds.Width <= 0)
        {
            return;
        }

        User32.GetCursorPos(out var cursor);
        var dx = cursor.X - _resizeStartCursor.X;
        var dy = cursor.Y - _resizeStartCursor.Y;
        var start = _resizeStartBounds;
        int left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
        if (_resizeEdge.Contains('L')) left += dx;
        if (_resizeEdge.Contains('R')) right += dx;
        if (_resizeEdge.Contains('T')) top += dy;
        if (_resizeEdge.Contains('B')) bottom += dy;

        var scale = PixelsPerUnit();
        var minWidth = (int)Math.Round(GuideWindowService.MinWidthDip * scale);
        var minHeight = (int)Math.Round(GuideWindowService.MinHeightDip * scale);
        if (right - left < minWidth)
        {
            if (_resizeEdge.Contains('L')) left = right - minWidth; else right = left + minWidth;
        }

        if (bottom - top < minHeight)
        {
            if (_resizeEdge.Contains('T')) top = bottom - minHeight; else bottom = top + minHeight;
        }

        // 沉浸模式按视频宽高比缩放，避免出现黑边；只拖上下边时由高度决定宽度
        if (ViewModel.State.IsImmersive && ViewModel.Config.LockAspectInImmersive && ViewModel.State.AspectRatio > 0)
        {
            var ratio = ViewModel.State.AspectRatio;
            if (_resizeEdge is "T" or "B")
            {
                var width = (int)Math.Round((bottom - top) * ratio);
                right = left + Math.Max(width, minWidth);
            }
            else
            {
                var height = (int)Math.Round((right - left) / ratio);
                height = Math.Max(height, minHeight);
                if (_resizeEdge.Contains('T')) top = bottom - height; else bottom = top + height;
            }
        }

        SetBounds(new RECT(left, top, right, bottom));
    }

    /// <summary>
    /// 缩放结束：保存位置和尺寸
    /// </summary>
    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        _resizeEdge = string.Empty;
        ViewModel.CommitBounds();
    }

    /// <summary>
    /// 界面单位与物理像素的换算比例
    /// </summary>
    private double PixelsPerUnit()
    {
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        return scale > 0 ? scale : 1;
    }

    #endregion

    /// <summary>
    /// 尺寸变化：更新圆角裁剪和窗口栏布局
    /// </summary>
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RootGrid.Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), CornerRadius, CornerRadius);
        ViewModel.UpdateBarLayout(ActualWidth);
    }

    /// <summary>
    /// 只有服务能关闭小窗；交互模式下按 Alt+F4 等关闭请求改为隐藏。程序退出时 WPF 忽略取消
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            ViewModel.OnCloseRequested();
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// 关闭后释放网页，停止播放并结束网页进程
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Detach();
        _bridge.Dispose();
        base.OnClosed(e);
    }
}
