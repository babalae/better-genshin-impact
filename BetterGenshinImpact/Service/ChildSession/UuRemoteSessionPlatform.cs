using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>
/// UU 修复所需的 Windows 集成；调用方负责在后台线程运行这些操作。
/// </summary>
internal sealed class UuRemoteSessionPlatform(
    InstanceContext context,
    ILogger<UuRemoteSessionRepairService> logger) : IUuRemoteSessionPlatform
{
    /// <summary>仅识别当前 UU 版本使用的串流进程名称。</summary>
    private const string StreamingProcessName = "GameViewerServer";

    /// <inheritdoc />
    public bool IsRoot => context.IsRoot;

    /// <inheritdoc />
    public bool IsElevated => RuntimeHelper.IsElevated;

    /// <inheritdoc />
    public int CurrentSessionId => context.WindowsSessionId;

    /// <inheritdoc />
    public uint? ConsoleSessionId
    {
        get
        {
            var sessionId = UuRemoteNativeMethods.WTSGetActiveConsoleSessionId();
            return sessionId == uint.MaxValue ? null : sessionId;
        }
    }

    /// <inheritdoc />
    public UuRemoteSessionSnapshot CaptureSnapshot()
    {
        var consoleSessionId = ConsoleSessionId;
        var sessions = UuRemoteNativeMethods.EnumerateSessions();
        using var identity = WindowsIdentity.GetCurrent();
        return new UuRemoteSessionSnapshot(
            consoleSessionId,
            CurrentSessionId,
            identity.User?.Value ?? string.Empty,
            identity.Name,
            sessions,
            GetStreamingProcesses());
    }

    /// <inheritdoc />
    public bool DisconnectSession(uint sessionId, out int errorCode)
        => UuRemoteNativeMethods.DisconnectSession(sessionId, out errorCode);

    /// <inheritdoc />
    public async Task StopStreamingProcessesAsync(
        uint consoleSessionId,
        Func<CancellationToken, Task> validateBeforeStopAsync,
        CancellationToken cancellationToken)
    {
        var processes = Process.GetProcessesByName(StreamingProcessName);
        try
        {
            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 多进程停止之间可能经历异步等待，不能沿用第一轮的用户或开关状态。
                await validateBeforeStopAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (ConsoleSessionId != consoleSessionId || CurrentSessionId != consoleSessionId)
                {
                    throw new InvalidOperationException("主桌面会话已发生变化，已停止 UU 会话修复。");
                }

                try
                {
                    if ((uint)process.SessionId == consoleSessionId)
                    {
                        continue;
                    }

                    logger.LogInformation(
                        "正在停止非主桌面会话中的 UU 串流进程 {ProcessId}（Session {SessionId}）。",
                        process.Id,
                        process.SessionId);
                    process.Kill(entireProcessTree: false);
                    // 超时必须取消底层退出等待，而不只是停止等待它，避免残留事件订阅。
                    using var exitWaitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    exitWaitCancellation.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        await process.WaitForExitAsync(exitWaitCancellation.Token).ConfigureAwait(false);
                        logger.LogInformation("UU 串流进程 {ProcessId} 已退出。", process.Id);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        logger.LogDebug("等待 UU 串流进程 {ProcessId} 退出超时，继续检查进程状态。", process.Id);
                    }
                }
                catch (InvalidOperationException)
                {
                    // 进程已经退出。
                }
                catch (Win32Exception exception)
                {
                    // 枚举后进程可能自行退出；其他权限或系统错误交给上层报告。
                    if (!process.HasExited)
                    {
                        throw new InvalidOperationException($"无法停止 UU 串流进程：{exception.Message}", exception);
                    }
                }
            }
        }
        finally
        {
            // 包括尚未遍历的 Process；失败、取消或提前退出时也不能遗留句柄。
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GameViewerServerInfo> GetStreamingProcesses()
    {
        var processes = Process.GetProcessesByName(StreamingProcessName);
        try
        {
            var result = new List<GameViewerServerInfo>();
            foreach (var process in processes)
            {
                try
                {
                    result.Add(new GameViewerServerInfo(process.Id, process.SessionId));
                }
                catch (InvalidOperationException)
                {
                    // 进程在枚举后自行退出，不计入本次快照。
                }
            }

            return result;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
