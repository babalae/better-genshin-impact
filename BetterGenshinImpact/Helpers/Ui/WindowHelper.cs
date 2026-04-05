using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask;
using Wpf.Ui.Controls;
using BetterGenshinImpact.Platform.Wine;

namespace BetterGenshinImpact.Helpers.Ui;

public class WindowHelper
{
    private const uint DesktopCompositionDisabledHResult = 0x80263001;

    public static void TryApplySystemBackdrop(System.Windows.Window window)
    {
        var themeType = TaskContext.Instance().Config.CommonConfig.CurrentThemeType;

        // Wine 平台适配
        if (WinePlatformAddon.IsRunningOnWine)
        {
            try
            {
                ApplyThemeToWindow(window, themeType);
            }
            catch
            {
                Debug.WriteLine($"Failed to apply theme in Wine");
            }
        }
        else
        {
            ApplyThemeToWindow(window, themeType);
        }
    }

    /// <summary>
    /// 根据主题类型应用主题到指定窗口
    /// </summary>
    /// <param name="window">要应用主题的窗口</param>
    /// <param name="themeType">主题类型</param>
    public static void ApplyThemeToWindow(System.Windows.Window window, ThemeType themeType)
    {
        try
        {
            ApplyThemeCore(window, themeType);
        }
        catch (COMException ex) when ((uint)ex.HResult == DesktopCompositionDisabledHResult)
        {
            ApplyFallbackTheme(window, themeType);
        }
        catch
        {
            ApplyFallbackTheme(window, themeType);
        }
    }

    private static void ApplyThemeCore(System.Windows.Window window, ThemeType themeType)
    {
        switch (themeType)
        {
            case ThemeType.DarkNone:
                window.Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));
                WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.None);
                break;

            case ThemeType.LightNone:
                window.Background = new SolidColorBrush(Color.FromArgb(255, 243, 243, 243));
                WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.None);
                break;

            case ThemeType.DarkMica:
                window.Background = new SolidColorBrush(Colors.Transparent);
                WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Mica);
                break;

            case ThemeType.LightMica:
                window.Background = new SolidColorBrush(Colors.Transparent);
                WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Mica);
                break;

            case ThemeType.DarkAcrylic:
                window.Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
                WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Acrylic);
                break;

            case ThemeType.LightAcrylic:
                window.Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
                WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Acrylic);
                break;

            default:
                window.Background = new SolidColorBrush(Colors.Transparent);
                WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.Mica);
                break;
        }
    }

    private static void ApplyFallbackTheme(System.Windows.Window window, ThemeType themeType)
    {
        window.Background = new SolidColorBrush(GetFallbackBackgroundColor(themeType));
        WindowBackdrop.ApplyBackdrop(window, WindowBackdropType.None);
    }

    private static Color GetFallbackBackgroundColor(ThemeType themeType)
    {
        return themeType switch
        {
            ThemeType.LightNone => Color.FromArgb(255, 243, 243, 243),
            ThemeType.LightMica => Color.FromArgb(255, 243, 243, 243),
            ThemeType.LightAcrylic => Color.FromArgb(255, 243, 243, 243),
            _ => Color.FromArgb(255, 32, 32, 32)
        };
    }
}
