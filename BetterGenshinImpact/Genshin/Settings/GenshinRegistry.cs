using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace BetterGenshinImpact.Genshin.Settings;

internal class GenshinRegistry
{
    private const string ChineseRegistryPath = @"SOFTWARE\miHoYo\原神";
    private const string GlobalRegistryPath = @"SOFTWARE\miHoYo\Genshin Impact";

    /// <summary>
    /// 自动模式优先根据正在运行的游戏进程判断区服，其次使用配置的游戏路径，最后根据注册表是否存在判断
    /// </summary>
    /// <param name="type"></param>
    /// <param name="gameExecutablePath">配置的原神可执行文件路径，用于自动判断区服</param>
    /// <returns></returns>
    public static RegistryKey? GetRegistryKey(GenshinRegistryType type = GenshinRegistryType.Auto, string? gameExecutablePath = null)
    {
        try
        {
            if (type == GenshinRegistryType.Auto)
            {
                type = ResolveRegistryType(gameExecutablePath);
            }

            if (type == GenshinRegistryType.Chinese)
            {
                return Registry.CurrentUser.OpenSubKey(ChineseRegistryPath, false);
            }

            if (type == GenshinRegistryType.Global)
            {
                return Registry.CurrentUser.OpenSubKey(GlobalRegistryPath, false);
            }

            if (type == GenshinRegistryType.Cloud)
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

    private static GenshinRegistryType ResolveRegistryType(string? gameExecutablePath)
    {
        bool isChineseRunning = IsProcessRunning("YuanShen");
        bool isGlobalRunning = IsProcessRunning("GenshinImpact");

        if (isChineseRunning != isGlobalRunning)
        {
            return isChineseRunning ? GenshinRegistryType.Chinese : GenshinRegistryType.Global;
        }

        string? executableName = Path.GetFileName(gameExecutablePath);
        if (string.Equals(executableName, "YuanShen.exe", StringComparison.OrdinalIgnoreCase))
        {
            return GenshinRegistryType.Chinese;
        }

        if (string.Equals(executableName, "GenshinImpact.exe", StringComparison.OrdinalIgnoreCase))
        {
            return GenshinRegistryType.Global;
        }

        using RegistryKey? chineseKey = Registry.CurrentUser.OpenSubKey(ChineseRegistryPath, false);
        using RegistryKey? globalKey = Registry.CurrentUser.OpenSubKey(GlobalRegistryPath, false);
        if (chineseKey is null && globalKey is not null)
        {
            return GenshinRegistryType.Global;
        }

        // 两个注册表同时存在或都不存在时保留原有的国服优先回退行为，用户仍可在键位映射页手动指定
        return GenshinRegistryType.Chinese;
    }

    private static bool IsProcessRunning(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }
}

public enum GenshinRegistryType
{
    Auto,
    Chinese,
    Global,
    Cloud,
}
