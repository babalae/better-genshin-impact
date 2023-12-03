using Microsoft.Win32;
using System;

namespace MicaSetup.Helper;

/// <summary>
/// https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/how-to-determine-which-versions-are-installed
/// </summary>
public static class DotNetVersionHelper
{
    public static Version? GetNet4xVersion()
    {
        using RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");

        if (key != null)
        {
            object? install = key.GetValue("Install");

            if (install != null && ((int)install) == 1)
            {
                object? version = key.GetValue("Version");

                if (version is string versionString)
                {
                    object? release = key.GetValue("Release");

                    if (release is int releaseValue)
                    {
                        if (releaseValue >= 533320)
                        {
                            return new Version(4, 8, 1);
                        }
                        else if (releaseValue >= 528040)
                        {
                            return new Version(4, 8, 0);
                        }
                        else if (releaseValue >= 461808)
                        {
                            return new Version(4, 7, 2);
                        }
                        else if (releaseValue >= 461308)
                        {
                            return new Version(4, 7, 1);
                        }
                        else if (releaseValue >= 460798)
                        {
                            return new Version(4, 7, 0);
                        }
                        else if (releaseValue >= 394802)
                        {
                            return new Version(4, 6, 2);
                        }
                        else if (releaseValue >= 394254)
                        {
                            return new Version(4, 6, 1);
                        }
                        else if (releaseValue >= 393295)
                        {
                            return new Version(4, 6, 0);
                        }
                        else if (releaseValue >= 379893)
                        {
                            return new Version(4, 5, 2);
                        }
                        else if (releaseValue >= 378675)
                        {
                            return new Version(4, 5, 1);
                        }
                        else if (releaseValue >= 378389)
                        {
                            return new Version(4, 5, 0);
                        }
                    }
                    return new Version(versionString);
                }
            }
            return new Version(4, 5);
        }
        return null!;
    }

    public static Version? GetNet3xVersion()
    {
        static Version? GetNet35Version()
        {
            using RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5");

            if (key != null)
            {
                object? install = key.GetValue("Install");

                if (install != null && ((int)install) == 1)
                {
                    object? version = key.GetValue("Version");

                    if (version is string versionString)
                    {
                        return new Version(versionString);
                    }
                }
                return new Version(3, 5);
            }
            return null!;
        }

        static Version? GetNet30Version()
        {
            RegistryKey key = null!;

            try
            {
                key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.0\SP2");
                key ??= Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.0\SP1");
                key ??= Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.0\Setup");

                if (key != null)
                {
                    object? install = key.GetValue("InstallSuccess");

                    if (install != null && ((int)install) == 1)
                    {
                        object? version = key.GetValue("Version");

                        if (version is string versionString)
                        {
                            return new Version(versionString);
                        }
                    }
                    return new Version(3, 0);
                }
                return null!;
            }
            finally
            {
                key?.Dispose();
            }
        }

        Version? version = GetNet35Version();
        if (version != null)
        {
            return version;
        }

        version = GetNet30Version();
        if (version != null)
        {
            return version;
        }
        return null!;
    }

    public static Version? GetNet2xVersion()
    {
        RegistryKey key = null!;

        try
        {
            key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v2.0.50727\SP2");
            key ??= Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v2.0.50727\SP1");
            key ??= Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v2.0.50727\Setup");

            if (key != null)
            {
                object? install = key.GetValue("Install");

                if (install != null && ((int)install) == 1)
                {
                    object? version = key.GetValue("Version");

                    if (version is string versionString)
                    {
                        return new Version(versionString);
                    }
                }
                return new Version(2, 0, 50727);
            }
            return null!;
        }
        finally
        {
            key?.Dispose();
        }
    }

    public static Version? GetNet1xVersion()
    {
        RegistryKey key = null!;

        try
        {
            key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v1.1.4322");
            if (key != null)
            {
                object? install = key.GetValue("Install");

                if (install != null && ((int)install) == 1)
                {
                    object? version = key.GetValue("Version");

                    if (version is string versionString)
                    {
                        return new Version(versionString);
                    }
                }
                return new Version(1, 1, 4322);
            }

            key ??= Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v1.0");
            if (key != null)
            {
                object? install = key.GetValue("Install");

                if (install != null && ((int)install) == 1)
                {
                    object? version = key.GetValue("Version");

                    if (version is string versionString)
                    {
                        return new Version(versionString);
                    }
                }
                return new Version(1, 0);
            }

            return null!;
        }
        finally
        {
            key?.Dispose();
        }
    }
}
