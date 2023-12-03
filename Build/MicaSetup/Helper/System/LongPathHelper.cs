using MicaSetup.Natives;
using Microsoft.Win32;

namespace MicaSetup.Helper;

public static class LongPathHelper
{
    /// <summary>
    /// Changes your machine configuration to allow programs, to pass the 260 character "MAX_PATH" limitation.
    /// </summary>
    public static bool EnableLongPath(string path)
    {
        return Kernel32.SetDllDirectory(@"\\?\" + path);
    }

    /// <summary>
    /// Changes your machine configuration to allow programs, to pass the 260 character "MAX_PATH" limitation.
    /// </summary>
    public static void EnableLongPath()
    {
        using RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem", true);
        key?.SetValue("LongPathsEnabled", 1, RegistryValueKind.DWord);
    }
}
