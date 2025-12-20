using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using BetterGenshinImpact.Platform.Wine;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Helpers.DpiAwareness;

/// <summary>
/// 高分辨率适配器
/// </summary>
internal class DpiAwarenessController
{
    private readonly Window window;

    private HwndSource? hwndSource;
    private HWND? hwnd;
    private double currentDpiRatio;

    static DpiAwarenessController()
    {
        if (WinePlatformAddon.IsRunningOnWine)
        {
            try
            {
                SHCore
                    .SetProcessDpiAwareness(
                        SHCore.PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE
                    )
                    .ThrowIfFailed();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Failed to set DPI awareness in Wine: {ex.Message}"
                );
            }
        }
        else{
          SHCore
              .SetProcessDpiAwareness(SHCore.PROCESS_DPI_AWARENESS.PROCESS_PER_MONITOR_DPI_AWARE)
              .ThrowIfFailed();
        }
    }

    /// <summary>
    /// 构造一个新的高分辨率适配器
    /// </summary>
    /// <param name="window">目标窗体</param>
    public DpiAwarenessController(Window window)
    {
        this.window = window;
        window.Loaded += (_, _) => OnAttached();
        window.Closing += (_, _) => OnDetaching();
    }

    private void OnAttached()
    {
        if (window.IsInitialized)
        {
            AddHwndHook();
        }
        else
        {
            window.SourceInitialized += AssociatedWindowSourceInitialized;
        }
    }

    private void OnDetaching()
    {
        RemoveHwndHook();
    }

    private void AddHwndHook()
    {
        hwndSource = PresentationSource.FromVisual(window) as HwndSource;
        hwndSource?.AddHook(HwndHook);
        hwnd = new WindowInteropHelper(window).Handle;
    }

    private void RemoveHwndHook()
    {
        window.SourceInitialized -= AssociatedWindowSourceInitialized;
        hwndSource?.RemoveHook(HwndHook);
        hwnd = null;
    }

    private void AssociatedWindowSourceInitialized(object? sender, EventArgs e)
    {
        AddHwndHook();

        currentDpiRatio = GetScaleRatio(window);
        UpdateDpiScaling(currentDpiRatio, true);
    }

    private unsafe nint HwndHook(nint hWnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is 0x02E0)
        {
            RECT rect = *(RECT*)&lParam;

            User32.SetWindowPosFlags flag =
                User32.SetWindowPosFlags.SWP_NOZORDER
                | User32.SetWindowPosFlags.SWP_NOACTIVATE
                | User32.SetWindowPosFlags.SWP_NOOWNERZORDER;
            User32.SetWindowPos(hWnd, default, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, flag);

            // we modified this fragment to correct the wrong behaviour
            double newDpiRatio = GetScaleRatio(window) * currentDpiRatio;
            if (newDpiRatio != currentDpiRatio)
            {
                UpdateDpiScaling(newDpiRatio);
            }
        }

        return default;
    }

    private void UpdateDpiScaling(double newDpiRatio, bool useSacleCenter = false)
    {
        currentDpiRatio = newDpiRatio;
        Debug.WriteLine($"Set dpi scaling to {currentDpiRatio:p2}");
        FrameworkElement firstChild = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        ScaleTransform transform;
        if (useSacleCenter)
        {
            double centerX = window.Left + (window.Width / 2);
            double centerY = window.Top + (window.Height / 2);

            transform = new ScaleTransform(currentDpiRatio, currentDpiRatio, centerX, centerY);
        }
        else
        {
            transform = new ScaleTransform(currentDpiRatio, currentDpiRatio);
        }

        firstChild.LayoutTransform = transform;
    }
    private static double GetScaleRatio(Window window)
    {
        PresentationSource hwndSource = PresentationSource.FromVisual(window);

        // TODO: verify use hwndSource there
        double wpfDpi = 96.0 * hwndSource.CompositionTarget.TransformToDevice.M11;

        HMONITOR hMonitor = User32.MonitorFromWindow(((HwndSource)hwndSource).Handle, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
        _ = SHCore.GetDpiForMonitor(hMonitor, SHCore.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out uint _);
        return dpiX / wpfDpi;
    }
}