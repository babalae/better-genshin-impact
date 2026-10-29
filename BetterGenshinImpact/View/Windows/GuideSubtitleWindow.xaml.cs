using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Guide;
using BetterGenshinImpact.ViewModel.Windows;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 字幕浮窗：透明置顶、鼠标穿透，按配置中的比例放在游戏画面里（见 Docs/design/guide-window.md 第 11 节）。
/// 调整模式下可以拖动位置、调整宽度和字号，结束时把位置换算为比例写入配置
/// </summary>
public partial class GuideSubtitleWindow : Window, IGuideSubtitleView
{
    /// <summary>
    /// 置顶层的 HWND_TOPMOST
    /// </summary>
    private static readonly HWND HwndTopmost = new(-1);

    /// <summary>
    /// 定位参照区域（物理像素）
    /// </summary>
    private RECT _anchor;

    /// <summary>
    /// 由服务关闭时允许关闭
    /// </summary>
    private bool _allowClose;

    /// <param name="viewModel">字幕浮窗 ViewModel</param>
    public GuideSubtitleWindow(GuideSubtitleWindowViewModel viewModel)
    {
        DataContext = ViewModel = viewModel;
        InitializeComponent();
        SizeChanged += (_, _) => Reposition();
        viewModel.Config.PropertyChanged += OnConfigChanged;
    }

    /// <summary>
    /// 字幕浮窗 ViewModel
    /// </summary>
    public GuideSubtitleWindowViewModel ViewModel { get; }

    /// <summary>
    /// 窗口句柄，调用时确保已创建
    /// </summary>
    private nint Handle => new WindowInteropHelper(this).EnsureHandle();

    #region IGuideSubtitleView

    /// <inheritdoc />
    public bool IsShown => IsVisible;

    /// <inheritdoc />
    public void ShowWithoutActivation()
    {
        if (!IsVisible)
        {
            Show();
            Reposition();
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
    public void UpdateAnchor(RECT anchor)
    {
        if (anchor.Width <= 0 || anchor.Height <= 0)
        {
            return;
        }

        _anchor = anchor;
        var maxWidthPx = anchor.Width * Math.Clamp(ViewModel.Config.SubtitleWidthRatio, 0.2, 1);
        ViewModel.MaxTextWidth = Math.Max(200, maxWidthPx / PixelsPerUnit());
        Reposition();
    }

    /// <inheritdoc />
    public void BringToTopmost()
    {
        _ = User32.SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0,
            User32.SetWindowPosFlags.SWP_NOMOVE | User32.SetWindowPosFlags.SWP_NOSIZE | User32.SetWindowPosFlags.SWP_NOACTIVATE);
    }

    #endregion

    /// <summary>
    /// 按配置中的比例放置：水平中心位于参照区域宽度的指定比例处，底边距参照区域底部指定比例
    /// </summary>
    private void Reposition()
    {
        if (_anchor.Width <= 0 || !User32.GetWindowRect(Handle, out var rect) || rect.Width <= 0)
        {
            return;
        }

        var config = ViewModel.Config;
        var centerX = _anchor.Left + _anchor.Width * Math.Clamp(config.SubtitleCenterXRatio, 0, 1);
        var bottom = _anchor.Bottom - _anchor.Height * Math.Clamp(config.SubtitleBottomRatio, 0, 1);
        var left = (int)Math.Round(centerX - rect.Width / 2d);
        var top = (int)Math.Round(bottom - rect.Height);
        left = Math.Clamp(left, _anchor.Left, Math.Max(_anchor.Left, _anchor.Right - rect.Width));
        top = Math.Clamp(top, _anchor.Top, Math.Max(_anchor.Top, _anchor.Bottom - rect.Height));
        if (left == rect.Left && top == rect.Top)
        {
            return;
        }

        _ = User32.SetWindowPos(Handle, HWND.NULL, left, top, 0, 0,
            User32.SetWindowPosFlags.SWP_NOSIZE | User32.SetWindowPosFlags.SWP_NOZORDER | User32.SetWindowPosFlags.SWP_NOACTIVATE);
    }

    /// <summary>
    /// 字号、宽度、位置配置变化时重新排版
    /// </summary>
    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GuideWindowConfig.SubtitleWidthRatio))
        {
            UpdateAnchor(_anchor);
        }
        else if (e.PropertyName is nameof(GuideWindowConfig.SubtitleCenterXRatio)
                 or nameof(GuideWindowConfig.SubtitleBottomRatio))
        {
            Reposition();
        }
    }

    /// <summary>
    /// 调整模式：按住拖动位置，松开后保存为比例
    /// </summary>
    private void OnRootMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!ViewModel.IsAdjusting || e.Handled)
        {
            return;
        }

        e.Handled = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        SavePosition();
    }

    /// <summary>
    /// 调整模式：Ctrl+滚轮调整字号
    /// </summary>
    private void OnRootMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!ViewModel.IsAdjusting || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        var config = ViewModel.Config;
        config.SubtitleFontSize = Math.Clamp(config.SubtitleFontSize + Math.Sign(e.Delta) * 2, 12, 48);
    }

    /// <summary>
    /// 调整模式：拖右侧边缘调整最大宽度
    /// </summary>
    private void OnWidthDragDelta(object sender, DragDeltaEventArgs e)
    {
        ViewModel.MaxTextWidth = Math.Max(200, Root.ActualWidth + e.HorizontalChange);
    }

    /// <summary>
    /// 宽度调整结束：保存为参照区域宽度的比例
    /// </summary>
    private void OnWidthDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_anchor.Width > 0)
        {
            ViewModel.Config.SubtitleWidthRatio = Math.Clamp(ViewModel.MaxTextWidth * PixelsPerUnit() / _anchor.Width, 0.2, 1);
        }

        SavePosition();
    }

    /// <summary>
    /// 把当前位置换算为参照区域内的比例并写入配置
    /// </summary>
    private void SavePosition()
    {
        if (_anchor.Width <= 0 || _anchor.Height <= 0 || !User32.GetWindowRect(Handle, out var rect))
        {
            return;
        }

        var config = ViewModel.Config;
        config.SubtitleCenterXRatio = Math.Clamp(((rect.Left + rect.Right) / 2d - _anchor.Left) / _anchor.Width, 0, 1);
        config.SubtitleBottomRatio = Math.Clamp((_anchor.Bottom - rect.Bottom) / (double)_anchor.Height, 0, 1);
    }

    /// <summary>
    /// 界面单位与物理像素的换算比例
    /// </summary>
    private double PixelsPerUnit()
    {
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? DpiFallback();
        return scale > 0 ? scale : 1;
    }

    /// <summary>
    /// 窗口尚未显示时按系统 DPI 换算
    /// </summary>
    private static double DpiFallback() => DpiHelper.GetScale().X;

    /// <summary>
    /// 只有服务能关闭字幕浮窗
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideWindow();
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// 关闭后取消订阅
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Config.PropertyChanged -= OnConfigChanged;
        ViewModel.Detach();
        base.OnClosed(e);
    }
}
