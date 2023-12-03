using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;

namespace MicaSetup.Helper;

#pragma warning disable CS8601

public static class RegistyUninstallHelper
{
    public static void Write(UninstallInfo info)
    {
        using RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Option.Current.IsUseRegistryPreferX86 switch
        {
            true => RegistryView.Registry32,
            false => RegistryView.Registry64,
            null or _ => RegistryView.Default,
        });
        using RegistryKey subKey = key.CreateSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{info.KeyName}");

        subKey.SetValue("DisplayName", info.DisplayName ?? string.Empty);
        subKey.SetValue("DisplayIcon", info.DisplayIcon ?? string.Empty);
        subKey.SetValue("DisplayVersion", info.DisplayVersion ?? string.Empty);
        subKey.SetValue("InstallLocation", info.InstallLocation ?? string.Empty);
        subKey.SetValue("Publisher", info.Publisher ?? string.Empty);
        subKey.SetValue("UninstallString", info.UninstallString ?? string.Empty);
        subKey.SetValue("UninstallData", info.UninstallData ?? string.Empty);
        subKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
        subKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        subKey.SetValue("SystemComponent", info.SystemComponent ? 1 : 0, RegistryValueKind.DWord);
    }

    public static UninstallInfo Read(string keyName)
    {
        UninstallInfo info = new()
        {
            KeyName = keyName,
        };

        using RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Option.Current.IsUseRegistryPreferX86 switch
        {
            true => RegistryView.Registry32,
            false => RegistryView.Registry64,
            null or _ => RegistryView.Default,
        });
        using RegistryKey subKey = key.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{info.KeyName}");

        info.DisplayName = subKey?.GetValue("DisplayName") as string;
        info.DisplayIcon = subKey?.GetValue("DisplayIcon") as string;
        info.InstallLocation = subKey?.GetValue("InstallLocation") as string;
        info.Publisher = subKey?.GetValue("Publisher") as string;
        info.UninstallString = subKey?.GetValue("UninstallString") as string;
        info.UninstallData = subKey?.GetValue("UninstallData") as string;
        return info;
    }

    public static void Delete(string keyName)
    {
        using RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Option.Current.IsUseRegistryPreferX86 switch
        {
            true => RegistryView.Registry32,
            false => RegistryView.Registry64,
            null or _ => RegistryView.Default,
        });
        using RegistryKey subKey = key.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{keyName}");
        if (subKey != null)
        {
            key.DeleteSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{keyName}");
        }
    }
}

public class UninstallInfo
{
    public string KeyName { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string DisplayIcon { get; set; } = null!;
    public string DisplayVersion { get; set; } = null!;
    public string InstallLocation { get; set; } = null!;
    public string Publisher { get; set; } = null!;
    public string UninstallString { get; set; } = null!;
    public string UninstallData { get; set; } = null!;
    public bool SystemComponent { get; set; } = false;
}

public class UninstallDataInfo
{
    public string InstallLocation { get; set; } = null!;
    public string UninstallData { get; set; } = null!;

    public string[] UninstallDataFullPath
    {
        get
        {
            List<string> paths = new();

            foreach (string uninstallDataCurrent in UninstallData?.Split('|', '\n') ?? Array.Empty<string>())
            {
                paths.Add(Path.Combine(InstallLocation ?? string.Empty, uninstallDataCurrent));
            }
            return paths.ToArray();
        }
    }

    public override string ToString() => UninstallData;
}
