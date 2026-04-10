using System;
using System.Diagnostics;
using System.IO;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Genshin.Paths;
using BetterGenshinImpact.Genshin.Settings;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BetterGenshinImpact.Service.Hdr;

public class HdrDetectionService
{
    public const string GameHdrRegistryValueName = "WINDOWS_HDR_ON_h3132281285";

    private readonly ILogger<HdrDetectionService> _logger = App.GetLogger<HdrDetectionService>();
    private readonly IConfigService _configService;

    public HdrDetectionService(IConfigService configService)
    {
        _configService = configService;
    }

    public HdrDetectionResult Detect(nint hWnd = 0)
    {
        string? gameExePath = ResolveGameExePath(hWnd);

        (bool isGameHdrKnown, bool gameHdrEnabled, string? gameHdrUnknownReason) = ReadGameHdrState(gameExePath);
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

    public static GenshinRegistryType ResolveRegistryType(string? gameExePath)
    {
        if (string.IsNullOrWhiteSpace(gameExePath))
        {
            return GenshinRegistryType.Auto;
        }

        string exeName = Path.GetFileName(gameExePath);
        if (string.Equals(exeName, "YuanShen.exe", StringComparison.OrdinalIgnoreCase))
        {
            return GenshinRegistryType.Chinese;
        }

        if (string.Equals(exeName, "GenshinImpact.exe", StringComparison.OrdinalIgnoreCase))
        {
            return GenshinRegistryType.Global;
        }

        return GenshinRegistryType.Auto;
    }

    public static bool? ParseGameHdrRegistryValue(object? rawValue)
    {
        return rawValue switch
        {
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            byte byteValue => byteValue != 0,
            string strValue when int.TryParse(strValue, out int parsed) => parsed != 0,
            byte[] { Length: >= 4 } byteArray => BitConverter.ToInt32(byteArray, 0) != 0,
            _ => null,
        };
    }

    private (bool IsKnown, bool Enabled, string? UnknownReason) ReadGameHdrState(string? gameExePath)
    {
        try
        {
            GenshinRegistryType registryType = ResolveRegistryType(gameExePath);
            using RegistryKey? registryKey = GenshinRegistry.GetRegistryKey(registryType);
            if (registryKey == null)
            {
                return (false, false, "无法读取游戏注册表中的 HDR 开关");
            }

            object? rawValue = registryKey.GetValue(GameHdrRegistryValueName);
            bool? hdrEnabled = ParseGameHdrRegistryValue(rawValue);
            if (!hdrEnabled.HasValue)
            {
                return (false, false, "无法解析游戏注册表中的 HDR 开关");
            }

            return (true, hdrEnabled.Value, null);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取游戏 HDR 设置失败");
            return (false, false, "读取游戏注册表中的 HDR 开关失败");
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
