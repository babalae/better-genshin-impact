using System;
using System.Diagnostics;
using System.IO;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Genshin.Paths;
using BetterGenshinImpact.Genshin.Settings2;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BetterGenshinImpact.Service.Hdr;

public class HdrDetectionService
{
    private readonly ILogger<HdrDetectionService> _logger = App.GetLogger<HdrDetectionService>();
    private readonly IConfigService _configService;

    public HdrDetectionService(IConfigService configService)
    {
        _configService = configService;
    }

    public HdrDetectionResult Detect(nint hWnd = 0)
    {
        string? gameExePath = ResolveGameExePath(hWnd);

        (bool isGameHdrKnown, bool gameHdrEnabled, string? gameHdrUnknownReason) = ReadGameHdrState();
        (bool isAutoHdrKnown, AutoHdrState appState, AutoHdrState globalState, string? autoHdrUnknownReason) =
            ReadAutoHdrState(gameExePath);
        (bool isDisplayHdrKnown, DisplayHdrState displayHdrState, string? displayHdrUnknownReason) =
            DisplayHdrStateReader.ReadCurrentDisplayHdrState(hWnd);

        return HdrDetectionEvaluator.Evaluate(
            isGameHdrKnown,
            gameHdrEnabled,
            isAutoHdrKnown,
            appState,
            globalState,
            isDisplayHdrKnown,
            displayHdrState,
            gameExePath,
            autoHdrUnknownReason,
            gameHdrUnknownReason,
            displayHdrUnknownReason);
    }

    public string? ResolveGameExePath(nint hWnd = 0)
    {
        string? processPath = TryGetProcessPath(hWnd);
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        string? configuredPath = _configService.Get().GenshinStartConfig.InstallPath;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string normalizedConfiguredPath = NormalizePath(configuredPath);
            if (File.Exists(normalizedConfiguredPath))
            {
                return normalizedConfiguredPath;
            }
        }

        string? registryPath = RegistryGameLocator.GetDefaultGameInstallPath();
        if (!string.IsNullOrWhiteSpace(registryPath))
        {
            return NormalizePath(registryPath);
        }

        return null;
    }

    private string? TryGetProcessPath(nint hWnd)
    {
        if (hWnd == 0)
        {
            return null;
        }

        try
        {
            Process? process = SystemControl.GetProcessByHandle(hWnd);
            string? processPath = process?.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return null;
            }

            return NormalizePath(processPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "通过窗口句柄读取游戏路径失败");
            return null;
        }
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }

    private (bool IsKnown, bool Enabled, string? UnknownReason) ReadGameHdrState()
    {
        try
        {
            string? settingStr = GenshinGameSettings.GetStrFromRegistry();
            if (string.IsNullOrWhiteSpace(settingStr))
            {
                return (false, false, "无法读取游戏设置注册表");
            }

            GenshinGameSettings? settings = GenshinGameSettings.Parse(settingStr);
            if (settings == null)
            {
                return (false, false, "无法解析游戏设置中的 HDR 配置");
            }

            return (true, settings.EnableHDR, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取游戏 HDR 设置失败");
            return (false, false, "读取游戏设置中的 HDR 配置失败");
        }
    }

    private (bool IsKnown, AutoHdrState AppState, AutoHdrState GlobalState, string? UnknownReason) ReadAutoHdrState(string? gameExePath)
    {
        if (string.IsNullOrWhiteSpace(gameExePath))
        {
            return (false, AutoHdrState.Unknown, AutoHdrState.Unknown, "无法确定当前游戏路径，无法判断 Windows Auto HDR 状态");
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UserGpuPreferencesParser.UserGpuPreferencesRegistryPath, false);
            if (key == null)
            {
                return (true, AutoHdrState.Unset, AutoHdrState.Unset, null);
            }

            string normalizedPath = NormalizePath(gameExePath);
            string? appRawValue = key.GetValue(normalizedPath) as string;
            string? globalRawValue = key.GetValue(UserGpuPreferencesParser.GlobalSettingsValueName) as string;

            AutoHdrState appState = UserGpuPreferencesParser.GetAutoHdrState(appRawValue);
            AutoHdrState globalState = UserGpuPreferencesParser.GetAutoHdrState(globalRawValue);
            return (true, appState, globalState, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取 Windows Auto HDR 配置失败");
            return (false, AutoHdrState.Unknown, AutoHdrState.Unknown, "读取 Windows 图形设置中的 Auto HDR 配置失败");
        }
    }
}
