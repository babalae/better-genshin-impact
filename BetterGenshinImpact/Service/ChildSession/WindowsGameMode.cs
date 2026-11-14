using System;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// 进入全屏前的游戏模式设置快照。
///
/// 记的是「值存不存在 + 原值」而不是布尔值：设置项之前可能根本没写过，
/// 还原时应当把它删掉，而不是写一个 0 进去——两者对用户意味着不同的状态。
/// </summary>
internal sealed class GameModeSnapshot
{
    internal bool KeyExisted { get; init; }

    internal bool AllowAutoGameModeExisted { get; init; }

    internal object? AllowAutoGameMode { get; init; }

    internal RegistryValueKind AllowAutoGameModeKind { get; init; }

    internal bool AutoGameModeEnabledExisted { get; init; }

    internal object? AutoGameModeEnabled { get; init; }

    internal RegistryValueKind AutoGameModeEnabledKind { get; init; }
}

/// <summary>
/// Windows「游戏模式」开关。
///
/// 全屏玩游戏时打开它，系统会把 CPU/GPU 优先给前台游戏、减少后台打扰；
/// 退出全屏后再还原成用户原来的设置——这是用户自己的系统偏好，程序没有理由长期改它。
///
/// 两个值都要动：<c>AllowAutoGameMode</c> 是「允许 Windows 自动打开游戏模式」，
/// <c>AutoGameModeEnabled</c> 是「使用游戏模式」，只开一个在实际游戏里不会生效。
/// </summary>
internal static class WindowsGameMode
{
    private const string GameBarKeyPath = @"Software\Microsoft\GameBar";
    private const string AllowAutoGameModeValueName = "AllowAutoGameMode";
    private const string AutoGameModeEnabledValueName = "AutoGameModeEnabled";

    /// <summary>
    /// 记录当前设置并打开游戏模式。
    /// 返回 null 表示没有发生需要还原的改动；非 null 时调用方负责在合适的时候还原。
    /// </summary>
    internal static GameModeSnapshot? TryEnable(ILogger logger)
    {
        // 读阶段单独 catch：读不到原值就不能动。把「存在但读不出」记成「不存在」，
        // 还原时会把用户原来的值删掉。
        GameModeSnapshot snapshot;
        try
        {
            using var readKey = Registry.CurrentUser.OpenSubKey(GameBarKeyPath);
            snapshot = new GameModeSnapshot
            {
                KeyExisted = readKey is not null,
                AllowAutoGameModeExisted = TryReadValue(readKey, AllowAutoGameModeValueName, out var allow, out var allowKind),
                AllowAutoGameMode = allow,
                AllowAutoGameModeKind = allowKind,
                AutoGameModeEnabledExisted = TryReadValue(readKey, AutoGameModeEnabledValueName, out var enabled, out var enabledKind),
                AutoGameModeEnabled = enabled,
                AutoGameModeEnabledKind = enabledKind
            };
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            logger.LogWarning(exception, "读取游戏模式设置失败，本次不启用游戏模式。");
            return null;
        }

        try
        {
            using var writeKey = Registry.CurrentUser.CreateSubKey(GameBarKeyPath);
            if (writeKey is null)
            {
                logger.LogWarning("无法打开游戏模式注册表项，本次不启用游戏模式。");
                return null;
            }

            writeKey.SetValue(AllowAutoGameModeValueName, 1, RegistryValueKind.DWord);
            writeKey.SetValue(AutoGameModeEnabledValueName, 1, RegistryValueKind.DWord);
            logger.LogInformation("已启用 Windows 游戏模式（全屏期间生效，退出后还原）。");
            return snapshot;
        }
        catch (Exception exception) when (IsRegistryException(exception))
        {
            // 可能只写成功了一半（例如两个值里第一个写进去了、第二个抛异常）。
            // 能回滚就回滚；回滚失败时把快照交回去，退出全屏时还有一次机会。
            if (Restore(snapshot, logger))
            {
                logger.LogWarning(exception, "启用 Windows 游戏模式失败，已回滚。");
                return null;
            }

            logger.LogWarning(exception, "启用 Windows 游戏模式失败，回滚也未完成，退出全屏时会再试一次。");
            return snapshot;
        }
    }

    /// <summary>
    /// 按快照还原。快照为 null 表示当时没改成功，直接算作已还原。
    /// </summary>
    /// <returns>
    /// 系统设置是否已经回到进入全屏前的状态。失败时调用方应当保留快照，
    /// 留到下次（例如关窗时）再试——直接丢掉就再也没人收尾了。
    /// </returns>
    internal static bool Restore(GameModeSnapshot? snapshot, ILogger logger)
    {
        if (snapshot is null)
        {
            return true;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GameBarKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            RestoreValue(
                key,
                AllowAutoGameModeValueName,
                snapshot.AllowAutoGameModeExisted,
                snapshot.AllowAutoGameMode,
                snapshot.AllowAutoGameModeKind);
            RestoreValue(
                key,
                AutoGameModeEnabledValueName,
                snapshot.AutoGameModeEnabledExisted,
                snapshot.AutoGameModeEnabled,
                snapshot.AutoGameModeEnabledKind);

            logger.LogInformation("已还原进入全屏前的 Windows 游戏模式设置。");
        }
        catch (Exception exception) when (exception is SecurityException
                                              or UnauthorizedAccessException
                                              or System.IO.IOException)
        {
            logger.LogWarning(exception, "还原 Windows 游戏模式设置失败，请手动检查系统设置。");
            return false;
        }

        // 这个键原本不存在时，我们的写入会把它创建出来；值都删干净后顺手收掉空键，
        // 免得给系统留下一个只有我们的痕迹的空项。
        if (!snapshot.KeyExisted)
        {
            try
            {
                using var check = Registry.CurrentUser.OpenSubKey(GameBarKeyPath);
                if (check is not null && check.ValueCount == 0 && check.SubKeyCount == 0)
                {
                    check.Close();
                    Registry.CurrentUser.DeleteSubKey(GameBarKeyPath, throwOnMissingSubKey: false);
                }
            }
            catch (Exception exception) when (exception is SecurityException
                                                  or UnauthorizedAccessException
                                                  or System.IO.IOException)
            {
                logger.LogDebug(exception, "清理空的游戏模式注册表项失败，忽略。");
            }
        }

        return true;
    }

    private static bool TryReadValue(
        RegistryKey? key,
        string name,
        out object? value,
        out RegistryValueKind kind)
    {
        value = null;
        kind = RegistryValueKind.DWord;

        if (key is null)
        {
            return false;
        }

        // 读失败不在这里吞掉：吞掉等于把「存在但读不出」当成「不存在」，
        // 还原时会把用户原来的值删掉。让调用方整体放弃这次启用更安全。
        // 不展开环境变量：ExpandString 类型的值展开后再写回会丢失原始 %VAR% 写法。
        value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null)
        {
            return false;
        }

        kind = key.GetValueKind(name);
        return true;
    }

    private static bool IsRegistryException(Exception exception)
    {
        return exception is SecurityException
            or UnauthorizedAccessException
            or System.IO.IOException;
    }

    private static void RestoreValue(
        RegistryKey key,
        string name,
        bool existed,
        object? value,
        RegistryValueKind kind)
    {
        if (existed && value is not null)
        {
            key.SetValue(name, value, kind);
            return;
        }

        key.DeleteValue(name, throwOnMissingValue: false);
    }
}
