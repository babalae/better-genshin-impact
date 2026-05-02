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
        var deletedPaths = new List<string>();
        foreach (var parentKeyPath in HdrRegistryParentKeyPaths)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(parentKeyPath, writable: true);
                if (key == null)
                {
                    continue;
                }

                if (key.GetValue(HdrRegistryEntryName) == null)
                {
                    continue;
                }

                key.DeleteValue(HdrRegistryEntryName, throwOnMissingValue: false);
                deletedPaths.Add($@"HKEY_CURRENT_USER\{parentKeyPath}\{HdrRegistryEntryName}");
            }
            catch
            {
                // 忽略删除失败，避免影响启动流程
            }
        }

        deletedFullKeyPaths = deletedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return deletedFullKeyPaths.Count > 0;
    }
}
