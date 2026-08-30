using BetterGenshinImpact.Core.Config;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace BetterGenshinImpact.GameTask;

/// <summary>
///     原神显示模式注册表助手。
///     原神 7.0+ 的显示设置以注册表为准，启动参数（-screen-width/-screen-height/-popupwindow）无法设置窗口模式；
///     且原神退出时会把自己的显示设置写回注册表，因此修改显示模式需要等游戏退出后再进行。
/// </summary>
public static class GenshinDisplayRegistryHelper
{
    public const string ResolutionWidthRegistryValueName = "Screenmanager Resolution Width_h182942802";
    public const string ResolutionHeightRegistryValueName = "Screenmanager Resolution Height_h2627697771";
    public const string FullscreenModeRegistryValueName = "Screenmanager Is Fullscreen mode_h3981298716";

    public const string CnDisplayRegistryParentKeyPath = @"Software\miHoYo\原神";
    public const string GlobalDisplayRegistryParentKeyPath = @"Software\miHoYo\Genshin Impact";

    /// <summary>
    ///     窗口化模式的固定分辨率
    /// </summary>
    public const int WindowedModeWidth = 1920;
    public const int WindowedModeHeight = 1080;

    public static readonly IReadOnlyList<string> DisplayRegistryParentKeyPaths =
    [
        CnDisplayRegistryParentKeyPath,
        GlobalDisplayRegistryParentKeyPath
    ];

    /// <summary>
    ///     各注册表路径对应的启动前显示设置快照（游戏退出后按此恢复）。
    ///     同时落盘到 User/Config，进程意外退出后下次启动仍可兜底恢复。
    /// </summary>
    private static readonly Dictionary<string, GenshinDisplaySettings> PreviousDisplaySettings = new();
    private static readonly object PreviousDisplaySettingsLock = new();
    private static readonly string SnapshotFilePath = Global.Absolute("User/Config/genshin_display_settings_snapshot.json");

    /// <summary>
    ///     记录各注册表路径当前的显示设置快照，并设置为固定分辨率窗口化模式
    /// </summary>
    public static bool CaptureAndSetWindowed()
    {
        var updated = false;
        foreach (var parentKeyPath in DisplayRegistryParentKeyPaths)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(parentKeyPath, writable: true);
                if (key == null)
                {
                    continue;
                }

                // 读不到完整快照时跳过，避免修改显示设置后无法恢复
                if (!TryGetDisplaySettings(key, out var settings))
                {
                    continue;
                }

                lock (PreviousDisplaySettingsLock)
                {
                    // 已有未恢复的快照时不覆盖（防止启动前游戏仍在运行时重复启动丢失原始设置）
                    PreviousDisplaySettings.TryAdd(parentKeyPath, settings);
                }

                key.SetValue(ResolutionWidthRegistryValueName, WindowedModeWidth, RegistryValueKind.DWord);
                key.SetValue(ResolutionHeightRegistryValueName, WindowedModeHeight, RegistryValueKind.DWord);
                key.SetValue(FullscreenModeRegistryValueName, 0, RegistryValueKind.DWord);
                updated = true;
            }
            catch
            {
                // 忽略写入失败，避免影响启动流程
            }
        }

        if (updated)
        {
            SaveSnapshotToDisk();
        }

        return updated;
    }

    /// <summary>
    ///     是否存在尚未恢复的显示设置快照（内存或磁盘）
    /// </summary>
    public static bool HasPendingDisplaySettings()
    {
        lock (PreviousDisplaySettingsLock)
        {
            return PreviousDisplaySettings.Count > 0 || File.Exists(SnapshotFilePath);
        }
    }

    /// <summary>
    ///     将显示设置恢复为启动前的快照（无快照时跳过），恢复后清空快照；
    ///     写回失败的路径保留在磁盘快照中，下次启动时兜底再试
    /// </summary>
    public static bool RestorePreviousDisplaySettings(out IReadOnlyList<GenshinDisplaySettings> restoredSettings)
    {
        // 进程内无快照时（如 BetterGI 重启后的兜底恢复）从磁盘加载
        LoadSnapshotFromDisk();

        var restored = new List<GenshinDisplaySettings>();
        Dictionary<string, GenshinDisplaySettings>? failedSnapshot = null;
        lock (PreviousDisplaySettingsLock)
        {
            var failed = new Dictionary<string, GenshinDisplaySettings>();
            foreach (var (parentKeyPath, settings) in PreviousDisplaySettings)
            {
                if (TryRestoreWithVerify(parentKeyPath, settings))
                {
                    restored.Add(settings);
                }
                else
                {
                    failed[parentKeyPath] = settings;
                }
            }

            PreviousDisplaySettings.Clear();
            if (failed.Count > 0)
            {
                failedSnapshot = failed;
            }
        }

        if (failedSnapshot != null)
        {
            SaveSnapshotToDisk(failedSnapshot);
        }
        else
        {
            DeleteSnapshotFile();
        }

        restoredSettings = restored;
        return restored.Count > 0;
    }

    /// <summary>
    ///     写回单个路径的显示设置，并读回验证（游戏退出时的写回可能与恢复竞争，验证不通过则重试）
    /// </summary>
    private static bool TryRestoreWithVerify(string parentKeyPath, GenshinDisplaySettings settings)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(parentKeyPath, writable: true);
                if (key == null)
                {
                    return false;
                }

                key.SetValue(ResolutionWidthRegistryValueName, settings.Width, RegistryValueKind.DWord);
                key.SetValue(ResolutionHeightRegistryValueName, settings.Height, RegistryValueKind.DWord);
                key.SetValue(FullscreenModeRegistryValueName, settings.FullscreenMode, RegistryValueKind.DWord);
                key.Flush();
            }
            catch
            {
                // 打开或写入失败，重试无意义
                return false;
            }

            try
            {
                using var readKey = Registry.CurrentUser.OpenSubKey(parentKeyPath);
                if (TryGetDisplaySettings(readKey, out var current) &&
                    current.Width == settings.Width &&
                    current.Height == settings.Height &&
                    current.FullscreenMode == settings.FullscreenMode)
                {
                    return true;
                }
            }
            catch
            {
                // 读回失败按未验证处理，稍后重试
            }

            Thread.Sleep(1000);
        }

        return false;
    }

    private static void SaveSnapshotToDisk()
    {
        Dictionary<string, GenshinDisplaySettings> snapshot;
        lock (PreviousDisplaySettingsLock)
        {
            snapshot = new Dictionary<string, GenshinDisplaySettings>(PreviousDisplaySettings);
        }

        SaveSnapshotToDisk(snapshot);
    }

    private static void SaveSnapshotToDisk(Dictionary<string, GenshinDisplaySettings> snapshot)
    {
        try
        {
            if (snapshot.Count == 0)
            {
                DeleteSnapshotFile();
                return;
            }

            var directory = Path.GetDirectoryName(SnapshotFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SnapshotFilePath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 快照落盘失败不阻断流程，内存快照仍可用
        }
    }

    private static void LoadSnapshotFromDisk()
    {
        try
        {
            if (!File.Exists(SnapshotFilePath))
            {
                return;
            }

            var snapshot = JsonSerializer.Deserialize<Dictionary<string, GenshinDisplaySettings>>(File.ReadAllText(SnapshotFilePath));
            if (snapshot == null)
            {
                return;
            }

            lock (PreviousDisplaySettingsLock)
            {
                foreach (var (path, settings) in snapshot)
                {
                    PreviousDisplaySettings.TryAdd(path, settings);
                }
            }
        }
        catch
        {
            // 快照损坏时按无快照处理
        }
    }

    private static void DeleteSnapshotFile()
    {
        try
        {
            if (File.Exists(SnapshotFilePath))
            {
                File.Delete(SnapshotFilePath);
            }
        }
        catch
        {
            // 删除失败无碍，下次启动兜底恢复时会再次尝试
        }
    }

    private static bool TryGetDisplaySettings(RegistryKey key, out GenshinDisplaySettings settings)
    {
        settings = default;
        if (!TryGetDWordValue(key, ResolutionWidthRegistryValueName, out var width) ||
            !TryGetDWordValue(key, ResolutionHeightRegistryValueName, out var height) ||
            !TryGetDWordValue(key, FullscreenModeRegistryValueName, out var fullscreenMode))
        {
            return false;
        }

        settings = new GenshinDisplaySettings(width, height, fullscreenMode);
        return true;
    }

    private static bool TryGetDWordValue(RegistryKey key, string name, out int value)
    {
        value = 0;
        try
        {
            switch (key.GetValue(name))
            {
                case int intValue:
                    value = intValue;
                    return true;
                case long longValue:
                    value = (int)longValue;
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
///     原神显示设置（注册表快照）
/// </summary>
public readonly record struct GenshinDisplaySettings(int Width, int Height, int FullscreenMode);
