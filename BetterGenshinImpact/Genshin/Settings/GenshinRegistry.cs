using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace BetterGenshinImpact.Genshin.Settings;

internal class GenshinRegistry
{
    public static RegistryKey? GetRegistryKey(GenshinRegistryType type = GenshinRegistryType.Auto)
    {
        try
        {
            using RegistryKey hkcu = Registry.CurrentUser;

            if (type == GenshinRegistryType.Auto)
            {
                {
                    if (hkcu.OpenSubKey(@"SOFTWARE\miHoYo\原神", true) is RegistryKey sk)
                    {
                        return sk;
                    }
                }
                {
                    if (hkcu.OpenSubKey(@"SOFTWARE\miHoYo\Genshin Impact", true) is RegistryKey sk)
                    {
                        return sk;
                    }
                }
            }
            else if (type == GenshinRegistryType.Chinese)
            {
                if (hkcu.OpenSubKey(@"SOFTWARE\miHoYo\原神", true) is RegistryKey sk)
                {
                    return sk;
                }
            }
            else if (type == GenshinRegistryType.Global)
            {
                if (hkcu.OpenSubKey(@"SOFTWARE\miHoYo\Genshin Impact", true) is RegistryKey sk)
                {
                    return sk;
                }
            }
            else if (type == GenshinRegistryType.Cloud)
            {
                throw new NotImplementedException();
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.ToString());
        }
        return null;
    }
}

public enum GenshinRegistryType
{
    Auto,
    Chinese,
    Global,
    Cloud,
}
