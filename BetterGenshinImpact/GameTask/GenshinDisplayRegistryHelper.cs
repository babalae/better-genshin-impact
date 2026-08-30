using Microsoft.Win32;
using System;
using System.Collections.Generic;

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

    public static readonly IReadOnlyList<string> DisplayRegistryParentKeyPaths =
    [
        CnDisplayRegistryParentKeyPath,
        GlobalDisplayRegistryParentKeyPath
    ];

    /// <summary>
    ///     各注册表路径对应的启动前显示设置快照（游戏退出后按此恢复）
    /// </summary>
    private static readonly Dictionary<string, GenshinDisplaySettings> PreviousDisplaySettings = new();
    private static readonly object PreviousDisplaySettingsLock = new();

    /// <summary>
    ///     记录各注册表路径当前的显示设置快照，并设置为窗口化模式
    /// </summary>
    public static bool CaptureAndSetWindowed(int width, int height)
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

                key.SetValue(ResolutionWidthRegistryValueName, width, RegistryValueKind.DWord);
                key.SetValue(ResolutionHeightRegistryValueName, height, RegistryValueKind.DWord);
                key.SetValue(FullscreenModeRegistryValueName, 0, RegistryValueKind.DWord);
                updated = true;
            }
            catch
            {
                // 忽略写入失败，避免影响启动流程
            }
        }

        return updated;
    }

    /// <summary>
    ///     将显示设置恢复为启动前的快照（无快照时跳过），恢复后清空快照
    /// </summary>
    public static bool RestorePreviousDisplaySettings(out IReadOnlyList<GenshinDisplaySettings> restoredSettings)
    {
        var restored = new List<GenshinDisplaySettings>();
        lock (PreviousDisplaySettingsLock)
        {
            foreach (var (parentKeyPath, settings) in PreviousDisplaySettings)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(parentKeyPath, writable: true);
                    if (key == null)
                    {
                        continue;
                    }

                    key.SetValue(ResolutionWidthRegistryValueName, settings.Width, RegistryValueKind.DWord);
                    key.SetValue(ResolutionHeightRegistryValueName, settings.Height, RegistryValueKind.DWord);
                    key.SetValue(FullscreenModeRegistryValueName, settings.FullscreenMode, RegistryValueKind.DWord);
                    restored.Add(settings);
                }
                catch
                {
                    // 忽略写入失败，避免影响启动流程
                }
            }

            PreviousDisplaySettings.Clear();
        }

        restoredSettings = restored;
        return restored.Count > 0;
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
