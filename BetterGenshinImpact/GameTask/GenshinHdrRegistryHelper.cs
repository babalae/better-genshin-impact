using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.GameTask;

internal enum GenshinGameEdition
{
    Unknown = 0,
    Cn = 1,
    Global = 2,
}

internal enum GenshinHdrRegistryValueState
{
    NotConfigured,
    Disabled,
    Enabled,
    Invalid,
    ReadFailed,
}

internal enum GenshinHdrDisableStatus
{
    NotConfigured,
    AlreadyDisabled,
    Disabled,
    PreparationFailed,
    ReadFailed,
    WriteFailed,
    UnsupportedEdition,
}

internal readonly record struct GenshinHdrRegistryReadResult(
    GenshinHdrRegistryValueState State,
    RegistryValueKind? ValueKind = null,
    Exception? Error = null);

internal readonly record struct GenshinHdrRegistryWriteResult(
    bool Success,
    Exception? Error = null);

internal readonly record struct GenshinHdrDisableResult(
    GenshinHdrDisableStatus Status,
    string? RegistryTarget,
    Exception? Error = null);

public static class GenshinHdrRegistryHelper
{
    internal const string CnProcessName = "YuanShen";
    internal const string GlobalProcessName = "GenshinImpact";
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

    internal static bool TryResolveEditionFromProcessName(
        string? processName,
        out GenshinGameEdition edition)
    {
        return TryResolveEditionFromExecutableName(processName, out edition);
    }

    internal static bool TryResolveEditionFromExecutablePath(
        string? executablePath,
        out GenshinGameEdition edition)
    {
        try
        {
            return TryResolveEditionFromExecutableName(
                Path.GetFileNameWithoutExtension(executablePath),
                out edition);
        }
        catch
        {
            edition = GenshinGameEdition.Unknown;
            return false;
        }
    }

    internal static string? GetHdrRegistryParentKeyPath(GenshinGameEdition edition)
    {
        return edition switch
        {
            GenshinGameEdition.Cn => CnHdrRegistryParentKeyPath,
            GenshinGameEdition.Global => GlobalHdrRegistryParentKeyPath,
            _ => null,
        };
    }

    internal static string? GetHdrRegistryFullValuePath(GenshinGameEdition edition)
    {
        var parentKeyPath = GetHdrRegistryParentKeyPath(edition);
        return parentKeyPath is null
            ? null
            : $@"HKEY_CURRENT_USER\{parentKeyPath}\{HdrRegistryEntryName}";
    }

    internal static GenshinHdrRegistryReadResult GetHdrState(GenshinGameEdition edition)
    {
        return ReadHdrRegistryValue(edition);
    }

    /// <summary>
    /// 关闭指定版本的原神 HDR。若传入 <paramref name="prepareBeforeWrite"/>，它会在注册表写入前执行，
    /// 用于先持久化“当前游戏必须重启”的安全标记，避免 BetterGI 在两步之间退出后错误放行 SDR 捕获。
    /// </summary>
    internal static GenshinHdrDisableResult TryDisableHdr(
        GenshinGameEdition edition,
        Func<string, bool>? prepareBeforeWrite = null)
    {
        return TryDisableHdr(
            edition,
            prepareBeforeWrite,
            ReadHdrRegistryValue,
            WriteDisabledHdrRegistryValue);
    }

    internal static GenshinHdrDisableResult TryDisableHdr(
        GenshinGameEdition edition,
        Func<string, bool>? prepareBeforeWrite,
        Func<GenshinGameEdition, GenshinHdrRegistryReadResult> readValue,
        Func<GenshinGameEdition, RegistryValueKind, GenshinHdrRegistryWriteResult> writeDisabledValue)
    {
        ArgumentNullException.ThrowIfNull(readValue);
        ArgumentNullException.ThrowIfNull(writeDisabledValue);

        var registryTarget = GetHdrRegistryFullValuePath(edition);
        if (registryTarget is null)
        {
            return new GenshinHdrDisableResult(
                GenshinHdrDisableStatus.UnsupportedEdition,
                null);
        }

        var readResult = readValue(edition);
        switch (readResult.State)
        {
            case GenshinHdrRegistryValueState.NotConfigured:
                return new GenshinHdrDisableResult(
                    GenshinHdrDisableStatus.NotConfigured,
                    registryTarget);
            case GenshinHdrRegistryValueState.Disabled:
                return new GenshinHdrDisableResult(
                    GenshinHdrDisableStatus.AlreadyDisabled,
                    registryTarget);
            case GenshinHdrRegistryValueState.Invalid:
            case GenshinHdrRegistryValueState.ReadFailed:
                return new GenshinHdrDisableResult(
                    GenshinHdrDisableStatus.ReadFailed,
                    registryTarget,
                    readResult.Error);
            case GenshinHdrRegistryValueState.Enabled:
                break;
            default:
                return new GenshinHdrDisableResult(
                    GenshinHdrDisableStatus.ReadFailed,
                    registryTarget,
                    new InvalidDataException("未知的原神 HDR 注册表状态。"));
        }

        if (readResult.ValueKind is not { } valueKind)
        {
            return new GenshinHdrDisableResult(
                GenshinHdrDisableStatus.ReadFailed,
                registryTarget,
                new InvalidDataException("原神 HDR 注册表值缺少数值类型。"));
        }

        if (prepareBeforeWrite is not null)
        {
            try
            {
                if (!prepareBeforeWrite(registryTarget))
                {
                    return new GenshinHdrDisableResult(
                        GenshinHdrDisableStatus.PreparationFailed,
                        registryTarget);
                }
            }
            catch (Exception e)
            {
                return new GenshinHdrDisableResult(
                    GenshinHdrDisableStatus.PreparationFailed,
                    registryTarget,
                    e);
            }
        }

        var writeResult = writeDisabledValue(edition, valueKind);
        return writeResult.Success
            ? new GenshinHdrDisableResult(GenshinHdrDisableStatus.Disabled, registryTarget)
            : new GenshinHdrDisableResult(
                GenshinHdrDisableStatus.WriteFailed,
                registryTarget,
                writeResult.Error);
    }

    private static GenshinHdrRegistryReadResult ReadHdrRegistryValue(
        GenshinGameEdition edition)
    {
        var parentKeyPath = GetHdrRegistryParentKeyPath(edition);
        if (parentKeyPath is null)
        {
            return new GenshinHdrRegistryReadResult(GenshinHdrRegistryValueState.NotConfigured);
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(parentKeyPath, writable: false);
            if (key == null)
            {
                return new GenshinHdrRegistryReadResult(GenshinHdrRegistryValueState.NotConfigured);
            }

            var value = key.GetValue(HdrRegistryEntryName);
            return value switch
            {
                null => new GenshinHdrRegistryReadResult(GenshinHdrRegistryValueState.NotConfigured),
                0 => new GenshinHdrRegistryReadResult(GenshinHdrRegistryValueState.Disabled),
                0L => new GenshinHdrRegistryReadResult(GenshinHdrRegistryValueState.Disabled),
                1 => new GenshinHdrRegistryReadResult(
                    GenshinHdrRegistryValueState.Enabled,
                    RegistryValueKind.DWord),
                1L => new GenshinHdrRegistryReadResult(
                    GenshinHdrRegistryValueState.Enabled,
                    RegistryValueKind.QWord),
                _ => new GenshinHdrRegistryReadResult(
                    GenshinHdrRegistryValueState.Invalid,
                    Error: new InvalidDataException(
                        $"原神 HDR 注册表值类型或内容无效：{value.GetType().Name}={value}")),
            };
        }
        catch (Exception e)
        {
            return new GenshinHdrRegistryReadResult(
                GenshinHdrRegistryValueState.ReadFailed,
                Error: e);
        }
    }

    private static GenshinHdrRegistryWriteResult WriteDisabledHdrRegistryValue(
        GenshinGameEdition edition,
        RegistryValueKind valueKind)
    {
        var parentKeyPath = GetHdrRegistryParentKeyPath(edition);
        if (parentKeyPath is null)
        {
            return new GenshinHdrRegistryWriteResult(
                false,
                new InvalidOperationException("未知的原神版本，无法写入 HDR 注册表值。"));
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(parentKeyPath, writable: true);
            if (key is null)
            {
                return new GenshinHdrRegistryWriteResult(
                    false,
                    new UnauthorizedAccessException($@"无法以可写方式打开注册表项 HKEY_CURRENT_USER\{parentKeyPath}。"));
            }

            // 保留原注册表数值类型，避免罕见的 QWORD 配置被改写成 DWord。
            var disabledValue = valueKind == RegistryValueKind.QWord ? (object)0L : 0;
            key.SetValue(HdrRegistryEntryName, disabledValue, valueKind);
            return new GenshinHdrRegistryWriteResult(true);
        }
        catch (Exception e)
        {
            return new GenshinHdrRegistryWriteResult(false, e);
        }
    }

    private static bool TryResolveEditionFromExecutableName(
        string? executableName,
        out GenshinGameEdition edition)
    {
        if (string.Equals(executableName, CnProcessName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(executableName, $"{CnProcessName}.exe", StringComparison.OrdinalIgnoreCase))
        {
            edition = GenshinGameEdition.Cn;
            return true;
        }

        if (string.Equals(executableName, GlobalProcessName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(executableName, $"{GlobalProcessName}.exe", StringComparison.OrdinalIgnoreCase))
        {
            edition = GenshinGameEdition.Global;
            return true;
        }

        edition = GenshinGameEdition.Unknown;
        return false;
    }
}
