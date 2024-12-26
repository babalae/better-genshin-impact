using System;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BetterGenshinImpact.Genshin.Paths;

/// <summary>
/// https://github.com/Scighost/Starward/blob/main/src%2FStarward%2FServices%2FLauncher%2FGameLauncherService.cs#L112-L112
/// </summary>
public class RegistryGameLocator
{
    public static string? GetDefaultGameInstallPath()
    {
        try
        {
            var cn = Registry.GetValue($@"HKEY_CURRENT_USER\Software\miHoYo\HYP\1_1\hk4e_cn", "GameInstallPath", null) as string;
            if (!string.IsNullOrEmpty(cn))
            {
                return cn;
            }

            var global = Registry.GetValue($@"HKEY_CURRENT_USER\Software\Cognosphere\HYP\1_0\hk4e_global", "GameInstallPath", null) as string;
            if (!string.IsNullOrEmpty(global))
            {
                return global;
            }

            var bilibili = Registry.GetValue($@"HKEY_CURRENT_USER\Software\miHoYo\HYP\standalone\14_0\hk4e_bilibili\BilibiliGenshin\hk4e_bilibili", "GameInstallPath", null) as string;
            if (!string.IsNullOrEmpty(bilibili))
            {
                return bilibili;
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogDebug(e, "Failed to locate game path.");
        }
        return null;
    }
}