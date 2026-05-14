using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask;

public static class GenshinHdrRegistryHelper
{
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

    public static bool TryDisableHdr(out IReadOnlyList<string> deletedFullKeyPaths)
    {
        var updatedPaths = new List<string>();
        foreach (var parentKeyPath in HdrRegistryParentKeyPaths)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(parentKeyPath, writable: true);
                if (key == null)
                {
                    continue;
                }

                var value = key.GetValue(HdrRegistryEntryName);
                if (value == null)
                {
                    continue;
                }

                var isHdrEnabled = value switch
                {
                    int intValue => intValue == 1,
                    long longValue => longValue == 1L,
                    _ => false
                };

                if (!isHdrEnabled)
                {
                    continue;
                }

                key.SetValue(HdrRegistryEntryName, 0, RegistryValueKind.DWord);
                updatedPaths.Add($@"HKEY_CURRENT_USER\{parentKeyPath}\{HdrRegistryEntryName}");
            }
            catch
            {
                // 忽略写入失败，避免影响启动流程
            }
        }

        deletedFullKeyPaths = updatedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return deletedFullKeyPaths.Count > 0;
    }
}
