using System;
using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Genshin.Paths;

internal class GameExePath
{
    /// <summary>
    /// 游戏路径（云原神除外）
    /// </summary>
    public static string? GetWithoutCloud()
    {
        return new[]
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\原神",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Genshin Impact",
            // @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\云·原神",
        }.Select(regKey => GetGameExePathFromRegistry(regKey, false)).FirstOrDefault(exePath => !string.IsNullOrEmpty(exePath));
    }

    /// <summary>
    /// 从注册表查找游戏路径
    /// </summary>
    /// <param name="key"></param>
    /// <param name="isCloud">云原神</param>
    /// <returns></returns>
    private static string? GetGameExePathFromRegistry(string key, bool isCloud)
    {
        try
        {
            var launcherPath = Registry.GetValue(key, "InstallPath", null) as string;
            if (isCloud)
            {
                var exeName = Registry.GetValue(key, "ExeName", null) as string;
                var exePath = Path.Join(launcherPath, exeName);
                if (File.Exists(exePath))
                {
                    return exePath;
                }
            }
            else
            {
                var configPath = Path.Join(launcherPath, "config.ini");
                if (File.Exists(configPath))
                {
                    var str = File.ReadAllText(configPath);
                    var installPath = Regex.Match(str, @"game_install_path=(.+)").Groups[1].Value.Trim();
                    var exeName = Regex.Match(str, @"game_start_name=(.+)").Groups[1].Value.Trim();
                    var exePath = Path.GetFullPath(exeName, installPath);
                    if (File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }
        }
        catch (Exception e)
        {
            TaskControl.Logger.LogWarning(e, "从注册表和启动器查找游戏路径失败");
        }

        return null;
    }
}
