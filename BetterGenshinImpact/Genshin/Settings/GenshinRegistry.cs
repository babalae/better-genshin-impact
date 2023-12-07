using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace BetterGenshinImpact.Genshin.Settings;

internal class GenshinRegistry
{
    public static RegistryKey? GetRegistryKey()
    {
        try
        {
            using RegistryKey hkcu = Registry.CurrentUser;

            if (hkcu.OpenSubKey(@"SOFTWARE\miHoYo\原神", true) is RegistryKey sk)
            {
                return sk;
            }
            else if (hkcu.OpenSubKey(@"SOFTWARE\miHoYo\Genshin Impact", true) is RegistryKey sk2)
            {
                return sk2;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.ToString());
        }
        return null;
    }
}
