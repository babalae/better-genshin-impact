using Microsoft.Win32;

namespace MicaSetup.Design.Controls;

public static class WindowsThemeHelper
{
    public static WindowsTheme GetCurrentWindowsTheme()
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        object? registryValueObject = key?.GetValue("AppsUseLightTheme");

        if (registryValueObject == null)
        {
            return WindowsTheme.Light;
        }

        var registryValue = (int)registryValueObject;

        return registryValue > 0 ? WindowsTheme.Light : WindowsTheme.Dark;
    }
}
