using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.DpiAwareness;
using BetterGenshinImpact.ViewModel.Windows;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Vanara.PInvoke;

namespace BetterGenshinImpact.View.Windows;

public partial class MapMiniFollowWindow : Window
{
    private const double BaseSizeAt1080P = 300;
    private const double MinSize = 240;
    private const double MaxSize = 640;
    private const double DefaultMargin = 18;
    private bool _isApplyingInitialLayout;

    public MapViewerViewModel ViewModel { get; }

    public MapMiniFollowWindow(MapViewerViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isApplyingInitialLayout = true;
        try
        {
            ApplyScaledSize();
            ApplyInitialPosition();
        }
        finally
        {
            _isApplyingInitialLayout = false;
        }

        ViewModel.ReplayMapDisplaySnapshot();
    }

    private void ApplyScaledSize()
    {
        var captureHeight = TryGetCaptureAreaRect(out var captureRect) ? captureRect.Height : 1080;
        var dpiScale = GetSafeDpiScaleY();
        var size = Math.Clamp(captureHeight / dpiScale * BaseSizeAt1080P / 1080d, MinSize, MaxSize);
        Width = size;
        Height = size;
    }

    private void ApplyInitialPosition()
    {
        var config = TaskContext.Instance().Config.DevConfig;
        if (IsUsableWindowPosition(config.MapMiniFollowLeft, config.MapMiniFollowTop))
        {
            Left = config.MapMiniFollowLeft;
            Top = config.MapMiniFollowTop;
            return;
        }

        if (!TryGetCaptureAreaRect(out var captureRect))
        {
            Left = SystemParameters.WorkArea.Right - Width - DefaultMargin;
            Top = SystemParameters.WorkArea.Top + DefaultMargin;
            return;
        }

        var (dpiX, dpiY) = GetSafeDpiScale();
        Left = Math.Max(SystemParameters.VirtualScreenLeft, captureRect.Left / dpiX + captureRect.Width / dpiX - Width - DefaultMargin);
        Top = Math.Max(SystemParameters.VirtualScreenTop, captureRect.Top / dpiY + DefaultMargin);
    }

    private static bool TryGetCaptureAreaRect(out RECT captureRect)
    {
        captureRect = default;

        try
        {
            var context = TaskContext.Instance();
            if (!context.IsInitialized || context.SystemInfo is null)
            {
                return false;
            }

            captureRect = context.SystemInfo.CaptureAreaRect;
            return captureRect.Width > 0 && captureRect.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static double GetSafeDpiScaleY()
    {
        try
        {
            return DpiHelper.ScaleY > 0 ? DpiHelper.ScaleY : 1;
        }
        catch
        {
            return 1;
        }
    }

    private static (double X, double Y) GetSafeDpiScale()
    {
        try
        {
            var dpiScale = DpiHelper.GetScale();
            return (dpiScale.X > 0 ? dpiScale.X : 1, dpiScale.Y > 0 ? dpiScale.Y : 1);
        }
        catch
        {
            return (1, 1);
        }
    }

    private static bool IsUsableWindowPosition(double left, double top)
    {
        return left >= SystemParameters.VirtualScreenLeft &&
               top >= SystemParameters.VirtualScreenTop &&
               left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
               top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_isApplyingInitialLayout)
        {
            return;
        }

        var config = TaskContext.Instance().Config.DevConfig;
        config.MapMiniFollowLeft = Left;
        config.MapMiniFollowTop = Top;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if the mouse state changes while the window is being closed.
        }
    }

    private void TopmostButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsMapMiniFollowTopmost = !ViewModel.IsMapMiniFollowTopmost;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsMapMiniFollowWindowVisible = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = User32.GetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE);
        _ = User32.SetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE, exStyle | (int)User32.WindowStylesEx.WS_EX_TOOLWINDOW);
    }
}
