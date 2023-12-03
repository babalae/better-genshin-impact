using MicaSetup.Helper;
using MicaSetup.Natives;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MicaSetup.Design.Controls;

public class ThemeService
{
    public static ThemeService Current { get; } = new();

    private WindowsTheme currentTheme = WindowsTheme.Auto;

    public WindowsTheme CurrentTheme
    {
        get => GetTheme();
        private set
        {
            currentTheme = value;
            ThemeResourceDictionary.SyncResource();
        }
    }

    public void EnableBackdrop(Window window, BackdropType micaType = BackdropType.Mica)
    {
        SetWindowBackdrop(window, micaType);
    }

    private void SetWindowBackdrop(Window window, BackdropType micaType)
    {
        if (!OsVersionHelper.IsWindows11_OrGreater)
        {
            return;
        }

        window.Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
        var windowHandle = new WindowInteropHelper(window).Handle;

        if (CurrentTheme == WindowsTheme.Dark)
        {
            NativeMethods.SetWindowAttribute(windowHandle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, 1);
        }
        else
        {
            NativeMethods.SetWindowAttribute(windowHandle, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, 0);
        }

        if (OsVersionHelper.IsWindows11_22523_OrGreater)
        {
            NativeMethods.SetWindowAttribute(windowHandle, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (int)micaType);
        }
        else
        {
            NativeMethods.SetWindowAttribute(windowHandle, DWMWINDOWATTRIBUTE.DWMWA_MICA_EFFECT, 1);
        }
    }

    private WindowsTheme GetTheme()
    {
        return currentTheme == WindowsTheme.Auto ? WindowsThemeHelper.GetCurrentWindowsTheme() : currentTheme;
    }

    public void SetTheme(WindowsTheme theme)
    {
        CurrentTheme = theme;
        SyncThemeResource();
    }

    private bool SyncThemeResource()
    {
        if (Application.Current == null)
        {
            return false;
        }

        string name = GetTheme().ToString();

        try
        {
            foreach (ResourceDictionary dictionary in Application.Current.Resources.MergedDictionaries)
            {
                if (dictionary is ThemeResourceDictionary trd)
                {
                    foreach (ResourceDictionary td in trd.MergedDictionaries)
                    {
                        if (dictionary.Source != null && dictionary.Source.OriginalString.Equals($"/Resources/Themes/{name}.xaml", StringComparison.Ordinal))
                        {
                            Application.Current.Resources.MergedDictionaries.Remove(dictionary);
                            Application.Current.Resources.MergedDictionaries.Add(dictionary);
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            _ = e;
        }
        return false;
    }
}
