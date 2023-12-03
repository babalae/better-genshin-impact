using Microsoft.Win32;

namespace MicaSetup.Helper;

public static class RegistyAutoRunHelper
{
    private const string RunLocation = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Enable(string keyName, string launchCommand)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunLocation);
        key?.SetValue(keyName, launchCommand);
    }

    public static bool IsEnabled(string keyName, string launchCommand)
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunLocation);

        if (key == null)
        {
            return false;
        }

        string value = (string)key.GetValue(keyName);

        if (value == null)
        {
            return false;
        }

        return value == launchCommand;
    }

    public static void Disable(string keyName, string launchCommand = null!)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunLocation);

        _ = launchCommand;
        if (key == null)
        {
            return;
        }

        if (key.GetValue(keyName) != null)
        {
            key.DeleteValue(keyName);
        }
    }

    public static void SetEnabled(bool enable, string keyName, string launchCommand)
    {
        if (enable)
        {
            Enable(keyName, launchCommand);
        }
        else
        {
            Disable(keyName);
        }
    }
}
