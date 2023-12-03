using MicaSetup.Helper;
using MicaSetup.Natives;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MicaSetup.Design.Controls;

public static class WindowBackdrop
{
    private static bool IsSupported(WindowBackdropType backdropType)
    {
        return backdropType switch
        {
            WindowBackdropType.Auto => OsVersionHelper.IsWindows11_22523,
            WindowBackdropType.Tabbed => OsVersionHelper.IsWindows11_22523,
            WindowBackdropType.Mica => OsVersionHelper.IsWindows11_OrGreater,
            WindowBackdropType.Acrylic => OsVersionHelper.IsWindows7_OrGreater,
            WindowBackdropType.None => OsVersionHelper.IsWindows11_OrGreater,
            _ => false
        };
    }

    public static bool ApplyBackdrop(Window window, WindowBackdropType backdropType = WindowBackdropType.Mica, ApplicationTheme theme = ApplicationTheme.Unknown)
    {
        if (!IsSupported(backdropType))
        {
            return false;
        }

        if (window is null)
        {
            return false;
        }

        if (window.IsLoaded)
        {
            nint windowHandle = new WindowInteropHelper(window).Handle;

            if (windowHandle == 0x00)
            {
                return false;
            }

            return ApplyBackdrop(windowHandle, backdropType, theme);
        }

        window.Loaded += (sender, _) =>
        {
            nint windowHandle =
                new WindowInteropHelper(sender as Window ?? null)?.Handle
                ?? IntPtr.Zero;

            if (windowHandle == 0x00)
                return;

            ApplyBackdrop(windowHandle, backdropType, theme);
        };

        return true;
    }

    public static bool ApplyBackdrop(nint hWnd, WindowBackdropType backdropType = WindowBackdropType.Mica, ApplicationTheme theme = ApplicationTheme.Unknown)
    {
        if (!IsSupported(backdropType))
        {
            return false;
        }

        if (hWnd == 0x00 || !User32.IsWindow(hWnd))
        {
            return false;
        }

        if (backdropType != WindowBackdropType.Auto)
        {
            WindowDarkMode.RemoveBackground(hWnd);
        }

        switch (theme)
        {
            case ApplicationTheme.Unknown:
                if (ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark)
                {
                    WindowDarkMode.ApplyWindowDarkMode(hWnd);
                }
                break;

            case ApplicationTheme.Dark:
                WindowDarkMode.ApplyWindowDarkMode(hWnd);
                break;

            case ApplicationTheme.Light:
            case ApplicationTheme.HighContrast:
                WindowDarkMode.RemoveWindowDarkMode(hWnd);
                break;
        }

        var wtaOptions = new UxTheme.WTA_OPTIONS()
        {
            Flags = UxTheme.WTNCA.WTNCA_NODRAWCAPTION,
            Mask = (uint)UxTheme.ThemeDialogTextureFlags.ETDT_VALIDBITS,
        };

        UxTheme.SetWindowThemeAttribute(
            hWnd,
            UxTheme.WINDOWTHEMEATTRIBUTETYPE.WTA_NONCLIENT,
            wtaOptions,
            (uint)Marshal.SizeOf(typeof(UxTheme.WTA_OPTIONS))
        );

        var dwmApiResult = DwmApi.DwmSetWindowAttribute(
            hWnd,
            DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE,
            (int)(backdropType switch
            {
                WindowBackdropType.Auto => DwmApi.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_AUTO,
                WindowBackdropType.Mica => DwmApi.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW,
                WindowBackdropType.Acrylic => DwmApi.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW,
                WindowBackdropType.Tabbed => DwmApi.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TABBEDWINDOW,
                _ => DwmApi.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_NONE,
            }),
            Marshal.SizeOf(typeof(int))
        );

        return dwmApiResult == HRESULT.S_OK;
    }

    public static bool RemoveBackdrop(Window window)
    {
        if (window == null)
        {
            return false;
        }

        nint hWnd = new WindowInteropHelper(window).Handle;

        return RemoveBackdrop(hWnd);
    }

    public static bool RemoveBackdrop(nint hWnd)
    {
        if (hWnd == 0x00 || !User32.IsWindow(hWnd))
        {
            return false;
        }

        var windowSource = HwndSource.FromHwnd(hWnd);

        if (windowSource?.Handle.ToInt32() != 0x00 && windowSource?.CompositionTarget != null)
        {
            windowSource.CompositionTarget.BackgroundColor = SystemColors.WindowColor;
        }

        if (windowSource?.RootVisual is Window window)
        {
            var backgroundBrush = window.Resources["ApplicationBackgroundBrush"];

            if (backgroundBrush is not SolidColorBrush)
            {
                backgroundBrush = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark
                                    ? new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x20, 0x20))
                                    : new SolidColorBrush(Color.FromArgb(0xFF, 0xFA, 0xFA, 0xFA));
            }

            window.Background = (SolidColorBrush)backgroundBrush;
        }

        _ = DwmApi.DwmSetWindowAttribute(
            hWnd,
            DWMWINDOWATTRIBUTE.DWMWA_MICA_EFFECT,
            0x0,
            Marshal.SizeOf(typeof(int))
        );

        _ = DwmApi.DwmSetWindowAttribute(
            hWnd,
            DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE,
            (int)DwmApi.DWM_SYSTEMBACKDROP_TYPE.DWMSBT_NONE,
            Marshal.SizeOf(typeof(int))
        );

        return true;
    }
}
