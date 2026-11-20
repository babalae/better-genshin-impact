using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 启动本地原神前关闭原生 HDR，并在启动完成后恢复临时修改的 Windows Auto HDR 配置。
/// </summary>
public static class GenshinHdrHelper
{
    private const string NativeHdrValueName = "WINDOWS_HDR_ON_h3132281285";
    private const string GpuPreferencesKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string AutoHdrFieldName = "AutoHDREnable";
    private const string AutoHdrDisabledValue = "2096";
    private static readonly TimeSpan AutoHdrRestoreDelay = TimeSpan.FromSeconds(10);

    public static async Task<nint> StartWithHdrDisabledAsync(
        string gamePath,
        Func<CancellationToken, Task<nint>> startGame,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(gamePath) || !File.Exists(gamePath))
        {
            return await startGame(cancellationToken);
        }

        gamePath = Path.GetFullPath(gamePath);
        var nativeKeyPath = Path.GetFileName(gamePath).ToLowerInvariant() switch
        {
            "yuanshen.exe" => @"Software\miHoYo\原神",
            "genshinimpact.exe" => @"Software\miHoYo\Genshin Impact",
            _ => null
        };
        if (nativeKeyPath == null || IsGameRunning(gamePath, logger))
        {
            return await startGame(cancellationToken);
        }

        TryDisableNativeHdr(nativeKeyPath, logger);
        var snapshot = TryDisableAutoHdr(gamePath, logger);
        try
        {
            var hWnd = await startGame(cancellationToken);
            if (snapshot != null && hWnd != 0)
            {
                await Task.Delay(AutoHdrRestoreDelay, cancellationToken);
            }

            return hWnd;
        }
        finally
        {
            // 取消启动也必须恢复配置，且恢复失败不能掩盖原启动异常。
            TryRestoreAutoHdr(snapshot, logger);
        }
    }

    private static bool IsGameRunning(string gamePath, ILogger logger)
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(gamePath));
            using var currentProcess = Process.GetCurrentProcess();
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId == currentProcess.SessionId)
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    // 枚举期间游戏已经退出。
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "无法确认原神是否已运行，跳过启动前 HDR 配置修改");
            return true;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void TryDisableNativeHdr(string keyPath, ILogger logger)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            var value = key?.GetValue(NativeHdrValueName);
            if (value is int and 1 or long and 1L)
            {
                key!.SetValue(NativeHdrValueName, 0, RegistryValueKind.DWord);
                logger.LogInformation("已关闭原神原生 HDR 配置");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "关闭原神原生 HDR 配置失败");
        }
    }

    private static AutoHdrSnapshot? TryDisableAutoHdr(string gamePath, ILogger logger)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(GpuPreferencesKeyPath);
            var value = key.GetValue(gamePath, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value != null && (value is not string || key.GetValueKind(gamePath) != RegistryValueKind.String))
            {
                logger.LogWarning("原神图形偏好不是字符串，跳过 Auto HDR 修改：{GamePath}", gamePath);
                return null;
            }

            if (!TryUpdateAutoHdrPreference((string?)value ?? "", AutoHdrDisabledValue,
                    out var updated, out var originalAutoHdr))
            {
                logger.LogWarning("原神图形偏好格式异常，跳过 Auto HDR 修改：{GamePath}", gamePath);
                return null;
            }

            if (originalAutoHdr == AutoHdrDisabledValue)
            {
                return null;
            }

            var snapshot = new AutoHdrSnapshot(gamePath, value != null, originalAutoHdr);
            key.SetValue(gamePath, updated, RegistryValueKind.String);
            logger.LogInformation("已为本次启动临时禁用原神 Auto HDR：{GamePath}", gamePath);
            return snapshot;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "临时禁用原神 Auto HDR 失败：{GamePath}", gamePath);
            return null;
        }
    }

    private static void TryRestoreAutoHdr(AutoHdrSnapshot? snapshot, ILogger logger)
    {
        if (snapshot == null)
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GpuPreferencesKeyPath, writable: true);
            var value = key?.GetValue(snapshot.GamePath, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null)
            {
                return; // 用户已删除临时配置，不重新创建。
            }

            if (value is not string preferences || key!.GetValueKind(snapshot.GamePath) != RegistryValueKind.String
                || !TryUpdateAutoHdrPreference(preferences, snapshot.OriginalAutoHdr,
                    out var updated, out var currentAutoHdr))
            {
                logger.LogWarning("原神图形偏好格式已变化，跳过 Auto HDR 恢复：{GamePath}", snapshot.GamePath);
                return;
            }

            if (currentAutoHdr != AutoHdrDisabledValue)
            {
                logger.LogDebug("原神 Auto HDR 已被外部修改，保留当前设置：{GamePath}", snapshot.GamePath);
                return;
            }

            // 仅还原目标字段，保留启动期间对 GPU 偏好等其他字段的修改。
            if (!snapshot.PreferenceValueExisted && updated.Length == 0)
            {
                key.DeleteValue(snapshot.GamePath, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(snapshot.GamePath, updated, RegistryValueKind.String);
            }

            logger.LogInformation("已恢复原神 Auto HDR 原始配置：{GamePath}", snapshot.GamePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "恢复原神 Auto HDR 配置失败：{GamePath}", snapshot.GamePath);
        }
    }

    /// <summary>
    /// 只替换或移除 Auto HDR 字段。null 表示字段缺失，未知数值按原字符串保留。
    /// </summary>
    internal static bool TryUpdateAutoHdrPreference(string preferences, string? autoHdr,
        out string updated, out string? originalAutoHdr)
    {
        updated = preferences;
        originalAutoHdr = null;
        var entries = preferences.Split(';').ToList();
        var autoHdrIndex = -1;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Length == 0)
            {
                continue;
            }

            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                return false;
            }

            if (!entry[..separator].Equals(AutoHdrFieldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (autoHdrIndex >= 0)
            {
                return false;
            }

            autoHdrIndex = i;
            originalAutoHdr = entry[(separator + 1)..];
        }

        if (autoHdrIndex >= 0)
        {
            if (autoHdr == null)
            {
                entries.RemoveAt(autoHdrIndex);
            }
            else
            {
                var entry = entries[autoHdrIndex];
                entries[autoHdrIndex] = entry[..(entry.IndexOf('=') + 1)] + autoHdr;
            }
        }
        else if (autoHdr != null)
        {
            // 保留原字符串是否带有末尾分号，移除临时字段时可还原原格式。
            var insertIndex = entries[^1].Length == 0 ? entries.Count - 1 : entries.Count;
            entries.Insert(insertIndex, $"{AutoHdrFieldName}={autoHdr}");
        }

        updated = string.Join(";", entries);
        return true;
    }

    private sealed record AutoHdrSnapshot(string GamePath, bool PreferenceValueExisted, string? OriginalAutoHdr);
}
