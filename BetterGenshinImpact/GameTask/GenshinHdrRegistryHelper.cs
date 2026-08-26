using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.GameTask;

internal enum GenshinGameEdition
{
    Unknown = 0,
    Cn = 1,
    Global = 2,
}

public static class GenshinHdrRegistryHelper
{
    internal const string CnProcessName = "YuanShen";
    internal const string GlobalProcessName = "GenshinImpact";
    public const string HdrRegistryEntryName = "WINDOWS_HDR_ON_h3132281285";
    public const string CnHdrRegistrySubKeyPath = @"Software\miHoYo\原神\WINDOWS_HDR_ON_h3132281285";
    public const string GlobalHdrRegistrySubKeyPath = @"Software\miHoYo\Genshin Impact\WINDOWS_HDR_ON_h3132281285";
    public const string CnHdrRegistryParentKeyPath = @"Software\miHoYo\原神";
    public const string GlobalHdrRegistryParentKeyPath = @"Software\miHoYo\Genshin Impact";

    public static readonly IReadOnlyList<string> HdrRegistrySubKeyPaths =
    [
        CnHdrRegistrySubKeyPath,
        GlobalHdrRegistrySubKeyPath
    ];

    public static readonly IReadOnlyList<string> HdrRegistryParentKeyPaths =
    [
        CnHdrRegistryParentKeyPath,
        GlobalHdrRegistryParentKeyPath
    ];

    public static IReadOnlyList<string> HdrRegistryFullKeyPaths =>
        HdrRegistrySubKeyPaths.Select(static path => $@"HKEY_CURRENT_USER\{path}").ToArray();

    internal static bool TryResolveEditionFromProcessName(
        string? processName,
        out GenshinGameEdition edition)
    {
        return TryResolveEditionFromExecutableName(processName, out edition);
    }

    internal static bool TryResolveEditionFromExecutablePath(
        string? executablePath,
        out GenshinGameEdition edition)
    {
        try
        {
            return TryResolveEditionFromExecutableName(
                Path.GetFileNameWithoutExtension(executablePath),
                out edition);
        }
        catch
        {
            edition = GenshinGameEdition.Unknown;
            return false;
        }
    }

    internal static string? GetHdrRegistryParentKeyPath(GenshinGameEdition edition)
    {
        return edition switch
        {
            GenshinGameEdition.Cn => CnHdrRegistryParentKeyPath,
            GenshinGameEdition.Global => GlobalHdrRegistryParentKeyPath,
            _ => null,
        };
    }

    internal static bool TryDisableHdr(
        GenshinGameEdition edition,
        out string? updatedFullKeyPath)
    {
        updatedFullKeyPath = null;
        var parentKeyPath = GetHdrRegistryParentKeyPath(edition);
        if (parentKeyPath is null)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(parentKeyPath, writable: true);
            if (key == null)
            {
                return false;
            }

            var value = key.GetValue(HdrRegistryEntryName);
            var disabledValue = value switch
            {
                1 => (object)0,
                1L => 0L,
                _ => null,
            };
            if (disabledValue is null)
            {
                return false;
            }

            // 保留原注册表数值类型，避免罕见的 QWORD 配置被改写成 DWord。
            var valueKind = value is long ? RegistryValueKind.QWord : RegistryValueKind.DWord;
            key.SetValue(HdrRegistryEntryName, disabledValue, valueKind);
            updatedFullKeyPath = $@"HKEY_CURRENT_USER\{parentKeyPath}\{HdrRegistryEntryName}";
            return true;
        }
        catch
        {
            // 忽略读写失败，避免注册表权限或瞬时错误阻断启动流程。
            return false;
        }
    }

    private static bool TryResolveEditionFromExecutableName(
        string? executableName,
        out GenshinGameEdition edition)
    {
        if (string.Equals(executableName, CnProcessName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(executableName, $"{CnProcessName}.exe", StringComparison.OrdinalIgnoreCase))
        {
            edition = GenshinGameEdition.Cn;
            return true;
        }

        if (string.Equals(executableName, GlobalProcessName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(executableName, $"{GlobalProcessName}.exe", StringComparison.OrdinalIgnoreCase))
        {
            edition = GenshinGameEdition.Global;
            return true;
        }

        edition = GenshinGameEdition.Unknown;
        return false;
    }
}
