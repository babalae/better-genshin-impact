using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.ChildSession;

public sealed class UuRemoteSessionRepairService
{
    private const string GameViewerServerProcessName = "GameViewerServer";
    private static readonly TimeSpan StreamingProcessRestartTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SessionDisconnectSettleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StartupMonitorDuration = TimeSpan.FromSeconds(60);

    private readonly InstanceService _instanceService;
    private readonly ChildSessionConfig _config;
    private readonly ILogger<UuRemoteSessionRepairService> _logger;
    private readonly SemaphoreSlim _repairGate = new(1, 1);
    private bool _isRepairing;
    private int _startupRepairAttemptCount;

    public UuRemoteSessionRepairService(
        InstanceService instanceService,
        IConfigService configService,
        ILogger<UuRemoteSessionRepairService> logger)
    {
        _instanceService = instanceService;
        _config = configService.Get().ChildSessionConfig;
        _logger = logger;
    }

    public event EventHandler<UuRemoteRepairStateChangedEventArgs>? RepairStateChanged;

    public bool IsEnabled => _config.AutoRepairUuRemoteSession;

    public bool IsRepairing => _isRepairing;

    public bool IsAvailable => _instanceService.Context.IsRoot
                               && RuntimeHelper.IsElevated
                               && (uint)GetCurrentProcessSessionId()
                               == GetConsoleSessionIdOrNull();

    internal bool IsRiskAcknowledged => _config.UuRemoteRepairRiskAcknowledged;

    internal void SetEnabled(bool enabled)
    {
        _config.AutoRepairUuRemoteSession = enabled;
    }

    internal void AcknowledgeRisk()
    {
        _config.UuRemoteRepairRiskAcknowledged = true;
    }

    internal void BeginStartupCycle()
    {
        Interlocked.Exchange(ref _startupRepairAttemptCount, 0);
    }

    internal async Task<UuRemoteSessionRepairResult> TryRepairBeforeStartAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "开始检查 UU 远程会话。已启用：{IsEnabled}，当前是否可修复：{IsAvailable}，"
            + "当前 Session：{CurrentSession}，控制台 Session：{ConsoleSession}。",
            IsEnabled,
            IsAvailable,
            GetCurrentProcessSessionId(),
            GetConsoleSessionIdOrNull());

        if (!IsEnabled)
        {
            return UuRemoteSessionRepairResult.NotNeeded();
        }

        await _repairGate.WaitAsync(cancellationToken);
        try
        {
            _isRepairing = true;
            RaiseState(UuRemoteRepairState.Checking, "正在检查 UU 远程会话状态…");

            var snapshot = await Task.Run(CaptureSnapshot, cancellationToken);
            var assessment = UuRemoteSessionEvaluator.Evaluate(snapshot);
            _logger.LogDebug(
                "UU 远程会话快照：当前 Session {CurrentSession}，控制台 Session {ConsoleSession}，"
                + "GameViewerServer Session [{GameViewerSessions}]，冲突：{HasConflict}，"
                + "待断开的当前用户后台 Session：[{SessionsToDisconnect}]。会话详情：{Sessions}。",
                snapshot.CurrentSessionId,
                snapshot.ConsoleSessionId,
                string.Join(", ", assessment.GameViewerSessions),
                assessment.HasConflict,
                string.Join(", ", assessment.SessionsToDisconnect),
                FormatSessions(snapshot.Sessions));
            if (!assessment.HasConflict)
            {
                RaiseState(UuRemoteRepairState.Idle, string.Empty);
                return UuRemoteSessionRepairResult.NotNeeded(
                    assessment.BlockingReason);
            }

            Interlocked.Exchange(ref _startupRepairAttemptCount, 1);
            if (!assessment.CanRepair)
            {
                var failureMessage = assessment.BlockingReason
                                     ?? "UU 远程会话修复未通过安全检查。";
                RaiseState(UuRemoteRepairState.Failed, failureMessage);
                _logger.LogWarning(
                    "跳过 UU 远程主会话修复：{Reason}。当前会话 {CurrentSession}，控制台会话 {ConsoleSession}。",
                    failureMessage,
                    snapshot.CurrentSessionId,
                    snapshot.ConsoleSessionId);
                return UuRemoteSessionRepairResult.Failed(
                    assessment.GameViewerSessions,
                    failureMessage);
            }

            var warningMessage =
                "检测到 UU 远程串流进程位于非主桌面会话。"
                + "BetterGI 将短暂断开当前用户的活动后台会话，并重启 UU 串流进程，"
                + "当前 UU 远程画面可能中断数秒。修复失败时仍会继续启动桌面分身。";
            RaiseState(UuRemoteRepairState.Repairing, warningMessage);
            _logger.LogWarning(
                "检测到 UU 远程主会话冲突，准备修复。UU 进程会话：{Sessions}。",
                string.Join(", ", assessment.GameViewerSessions));

            var result = await RepairCoreAsync(
                snapshot,
                assessment,
                cancellationToken);
            if (result.IsSuccessful)
            {
                RaiseState(
                    UuRemoteRepairState.Succeeded,
                    "UU 远程主会话冲突已修复，正在继续启动桌面分身。");
            }
            else
            {
                RaiseState(
                    UuRemoteRepairState.Failed,
                    result.FailureReason ?? "UU 远程主会话修复失败。");
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var actualException = exception.GetBaseException();
            RaiseState(
                UuRemoteRepairState.Failed,
                $"UU 远程主会话修复失败：{actualException.Message}");
            _logger.LogWarning(exception, "UU 远程主会话修复失败");
            return UuRemoteSessionRepairResult.Failed(
                [],
                actualException.Message);
        }
        finally
        {
            _isRepairing = false;
            _repairGate.Release();
        }
    }

    internal async Task<UuRemoteSessionRepairResult> CheckAfterStartAsync(
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return UuRemoteSessionRepairResult.NotNeeded();
        }

        try
        {
            // UU 可能在 RDP 登录完成后才启动或切换 GameViewerServer 的 Session。
            // 在启动后的短窗口内继续观察，避免因几秒钟的时序差漏掉冲突。
            var deadline = DateTime.UtcNow + StartupMonitorDuration;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(PollInterval, cancellationToken);
                var snapshot = await Task.Run(CaptureSnapshot, cancellationToken);
                var assessment = UuRemoteSessionEvaluator.Evaluate(snapshot);
                if (!assessment.HasConflict)
                {
                    continue;
                }

                if (Volatile.Read(ref _startupRepairAttemptCount) != 0)
                {
                    RaiseState(
                        UuRemoteRepairState.PostStartConflict,
                        "桌面分身启动后检测到 UU 串流进程仍位于非主桌面会话。"
                        + "本次启动已经执行过一次修复，不会再次自动修复，请重新连接 UU 或在下次启动桌面分身时处理。");
                    return UuRemoteSessionRepairResult.NotNeeded();
                }

                _logger.LogInformation(
                    "桌面分身启动后发现 UU 远程会话冲突，开始本次启动的首次修复。"
                    + "GameViewerServer Session：[{GameViewerSessions}]。",
                    string.Join(", ", assessment.GameViewerSessions));
                return await TryRepairBeforeStartAsync(cancellationToken);
            }

            _logger.LogDebug(
                "桌面分身启动后的 {DurationSeconds} 秒内未检测到 UU 远程会话冲突。",
                StartupMonitorDuration.TotalSeconds);
            return UuRemoteSessionRepairResult.NotNeeded();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "桌面分身启动后复查 UU 远程会话失败");
            return UuRemoteSessionRepairResult.NotNeeded();
        }
    }

    private async Task<UuRemoteSessionRepairResult> RepairCoreAsync(
        UuRemoteSessionSnapshot snapshot,
        UuRemoteSessionAssessment assessment,
        CancellationToken cancellationToken)
    {
        if (!_instanceService.Context.IsRoot)
        {
            return UuRemoteSessionRepairResult.Failed(
                assessment.GameViewerSessions,
                "只有 BetterGI 根实例可以修复 UU 远程主会话。");
        }

        if (!RuntimeHelper.IsElevated)
        {
            return UuRemoteSessionRepairResult.Failed(
                assessment.GameViewerSessions,
                "BetterGI 需要以管理员权限运行才能修复 UU 远程主会话。");
        }

        if (snapshot.ConsoleSessionId is null)
        {
            return UuRemoteSessionRepairResult.Failed(
                assessment.GameViewerSessions,
                "无法确定当前的主桌面会话。");
        }

        if (snapshot.CurrentSessionId != snapshot.ConsoleSessionId.Value)
        {
            return UuRemoteSessionRepairResult.Failed(
                assessment.GameViewerSessions,
                "BetterGI 当前不在主桌面会话中，已跳过 UU 远程会话修复。");
        }

        var originalSessions = assessment.GameViewerSessions;
        if (assessment.SessionsToDisconnect.Count > 0
            && !CanDisconnectSessionsBeforeRepair(
                assessment.SessionsToDisconnect,
                snapshot,
                out var blockingReason))
        {
            _logger.LogWarning(
                "断开 Windows 会话前复核失败：{Reason}。已取消本次 UU 远程会话修复。",
                blockingReason);
            return UuRemoteSessionRepairResult.Failed(
                originalSessions,
                blockingReason);
        }

        foreach (var sessionId in assessment.SessionsToDisconnect)
        {
            if (!UuRemoteNativeMethods.DisconnectSession(
                    sessionId,
                    out var errorCode))
            {
                var message =
                    $"无法断开当前用户的后台 Windows 会话 {sessionId}。"
                    + $"Win32 错误：{errorCode}。";
                _logger.LogWarning(
                    "{Message} 修复已跳过，避免在 Session 状态未清理时重启 UU 串流进程。",
                    message);
                return UuRemoteSessionRepairResult.Failed(originalSessions, message);
            }

            _logger.LogInformation(
                "已断开当前用户的 UU 后台 Windows 会话 {SessionId}。",
                sessionId);
        }

        if (assessment.SessionsToDisconnect.Count > 0)
        {
            await Task.Delay(SessionDisconnectSettleDelay, cancellationToken);
        }

        var processes = GetGameViewerServerProcessesInNonConsoleSessions(
            snapshot.ConsoleSessionId.Value);
        if (processes.Length == 0)
        {
            // GameViewerServer 可能刚好自行退出。这不是失败：
            // UU 服务仍可能在随后重新拉起进程，因此继续等待。
            _logger.LogInformation(
                "修复时未找到仍在运行的 UU 串流进程，继续等待 UU 服务重新启动。");
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    _logger.LogInformation(
                        "正在停止非主桌面会话中的 UU 串流进程 {ProcessId}（Session {SessionId}）。",
                        process.Id,
                        process.SessionId);
                    process.Kill(entireProcessTree: false);
                    await process.WaitForExitAsync(cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                    _logger.LogInformation(
                        "UU 串流进程 {ProcessId} 已退出。",
                        process.Id);
                }
                catch (TimeoutException)
                {
                    _logger.LogDebug(
                        "等待 UU 串流进程 {ProcessId} 退出超时，继续检查进程状态。",
                        process.Id);
                }
                catch (InvalidOperationException)
                {
                    // 进程已经退出。
                }
                catch (Win32Exception exception)
                {
                    var message = $"无法停止 UU 串流进程：{exception.Message}";
                    _logger.LogWarning(exception, "{Message}", message);
                    return UuRemoteSessionRepairResult.Failed(originalSessions, message);
                }
            }
        }

        var deadline = DateTime.UtcNow + StreamingProcessRestartTimeout;
        IReadOnlyList<int> lastSessions = [];
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken);
            lastSessions = GetGameViewerServerSessions();
            if (lastSessions.Count > 0
                && lastSessions.All(x => x == snapshot.ConsoleSessionId.Value))
            {
                _logger.LogInformation(
                    "UU 远程主会话修复成功，GameViewerServer 已回到会话 {SessionId}。",
                    snapshot.ConsoleSessionId);
                return UuRemoteSessionRepairResult.Successful(
                    originalSessions,
                    lastSessions);
            }
        }

        var failureReason =
            $"UU 串流进程未能在 {StreamingProcessRestartTimeout.TotalSeconds:0} 秒内回到主桌面会话。"
            + (lastSessions.Count == 0
                ? "当前未检测到 GameViewerServer 进程。"
                : $"当前会话：{string.Join(", ", lastSessions)}。");
        try
        {
            var finalSessions = UuRemoteNativeMethods.EnumerateSessions();
            var finalViewerProcesses = GetGameViewerServerProcesses();
            _logger.LogWarning(
                "{Message} 当前活动会话：{ActiveSessions}。GameViewerServer：{GameViewerProcesses}。",
                failureReason,
                FormatSessions(finalSessions),
                string.Join(
                    ", ",
                    finalViewerProcesses.Select(
                        x => $"PID {x.ProcessId}/Session {x.SessionId}")));
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "收集 UU 远程会话修复失败诊断信息时出错");
        }

        _logger.LogWarning("{Message}", failureReason);
        return UuRemoteSessionRepairResult.Failed(originalSessions, failureReason);
    }

    private static bool CanDisconnectSessionsBeforeRepair(
        IReadOnlyList<uint> sessionIds,
        UuRemoteSessionSnapshot snapshot,
        out string blockingReason)
    {
        var sessions = UuRemoteNativeMethods.EnumerateSessions();
        if (sessions.Any(x =>
                x.IsActive
                && x.SessionId != 0
                && !x.IsConsole
                && !UuRemoteSessionEvaluator.IsCurrentUser(snapshot, x)))
        {
            blockingReason =
                "检测到其他 Windows 用户或无法确认所有者的活动会话，已取消修复以避免影响其他用户。";
            return false;
        }

        foreach (var sessionId in sessionIds)
        {
            var session = sessions.FirstOrDefault(x => x.SessionId == sessionId);
            if (session is null)
            {
                blockingReason = $"Windows 会话 {sessionId} 已不存在，已取消本次修复。";
                return false;
            }

            if (session.IsConsole)
            {
                blockingReason =
                    $"Windows 会话 {sessionId} 已变为主控制台会话，已取消本次修复。";
                return false;
            }

            if (!session.IsActive)
            {
                blockingReason =
                    $"Windows 会话 {sessionId} 已不再处于活动状态，已取消本次修复。";
                return false;
            }

            if (!UuRemoteSessionEvaluator.IsCurrentUser(snapshot, session))
            {
                blockingReason =
                    $"Windows 会话 {sessionId} 的当前所有者已不是 BetterGI 所在用户，已取消本次修复。";
                return false;
            }
        }

        blockingReason = string.Empty;
        return true;
    }

    private UuRemoteSessionSnapshot CaptureSnapshot()
    {
        var consoleSessionId = GetConsoleSessionIdOrNull();
        var sessions = UuRemoteNativeMethods.EnumerateSessions();
        using var identity = WindowsIdentity.GetCurrent();
        return new UuRemoteSessionSnapshot(
            consoleSessionId,
            GetCurrentProcessSessionId(),
            identity.User?.Value ?? string.Empty,
            identity.Name,
            sessions,
            GetGameViewerServerProcesses());
    }

    private static int GetCurrentProcessSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    private static uint? GetConsoleSessionIdOrNull()
    {
        var sessionId = UuRemoteNativeMethods.WTSGetActiveConsoleSessionId();
        return sessionId == uint.MaxValue ? null : sessionId;
    }

    private static IReadOnlyList<GameViewerServerInfo> GetGameViewerServerProcesses()
    {
        return Process.GetProcessesByName(GameViewerServerProcessName)
            .Select(process =>
            {
                using (process)
                {
                    try
                    {
                        return new GameViewerServerInfo(
                            process.Id,
                            process.SessionId);
                    }
                    catch (InvalidOperationException)
                    {
                        return null;
                    }
                }
            })
            .Where(x => x is not null)
            .Cast<GameViewerServerInfo>()
            .ToArray();
    }

    private static IReadOnlyList<int> GetGameViewerServerSessions()
    {
        return GetGameViewerServerProcesses()
            .Select(x => x.SessionId)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
    }

    private static Process[] GetGameViewerServerProcessesInNonConsoleSessions(
        uint consoleSessionId)
    {
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName(GameViewerServerProcessName))
        {
            try
            {
                if ((uint)process.SessionId != consoleSessionId)
                {
                    result.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch (InvalidOperationException)
            {
                process.Dispose();
            }
        }

        return result.ToArray();
    }

    private static string FormatSessions(IReadOnlyList<WindowsSessionInfo> sessions)
    {
        return string.Join(
            "; ",
            sessions.Select(x =>
                $"{x.SessionId}:{x.State}:{x.UserName}"));
    }

    private void RaiseState(UuRemoteRepairState state, string message)
    {
        RepairStateChanged?.Invoke(
            this,
            new UuRemoteRepairStateChangedEventArgs(state, message));
    }
}

internal sealed record UuRemoteSessionRepairResult(
    bool WasAttempted,
    bool IsSuccessful,
    IReadOnlyList<int> OriginalSessions,
    IReadOnlyList<int> RepairedSessions,
    string? FailureReason)
{
    internal static UuRemoteSessionRepairResult NotNeeded(string? reason = null)
    {
        return new UuRemoteSessionRepairResult(
            false,
            true,
            [],
            [],
            reason);
    }

    internal static UuRemoteSessionRepairResult Successful(
        IReadOnlyList<int> originalSessions,
        IReadOnlyList<int> repairedSessions)
    {
        return new UuRemoteSessionRepairResult(
            true,
            true,
            originalSessions,
            repairedSessions,
            null);
    }

    internal static UuRemoteSessionRepairResult Failed(
        IReadOnlyList<int> originalSessions,
        string reason)
    {
        return new UuRemoteSessionRepairResult(
            true,
            false,
            originalSessions,
            [],
            reason);
    }
}

internal static class UuRemoteNativeMethods
{
    private const int WtsInfoUserName = 5;
    private const int WtsInfoDomainName = 7;
    private const int WtsInfoUserSid = 22;

    [DllImport("kernel32.dll")]
    internal static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(
        IntPtr serverHandle,
        int reserved,
        int version,
        out IntPtr sessionInfo,
        out int count);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr serverHandle,
        int sessionId,
        int infoClass,
        out IntPtr buffer,
        out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSDisconnectSession(
        IntPtr serverHandle,
        int sessionId,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    internal static bool DisconnectSession(uint sessionId, out int errorCode)
    {
        if (WTSDisconnectSession(IntPtr.Zero, (int)sessionId, false))
        {
            errorCode = 0;
            return true;
        }

        errorCode = Marshal.GetLastPInvokeError();
        return false;
    }

    internal static IReadOnlyList<WindowsSessionInfo> EnumerateSessions()
    {
        if (!WTSEnumerateSessions(
                IntPtr.Zero,
                0,
                1,
                out var sessionInfo,
                out var count))
        {
            var error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(
                error,
                $"无法枚举 Windows 会话（Win32 错误 {error}）。");
        }

        try
        {
            var sessions = new WindowsSessionInfo[count];
            var structSize = Marshal.SizeOf<WtsSessionInfo>();
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<WtsSessionInfo>(
                    IntPtr.Add(sessionInfo, index * structSize));
                sessions[index] = new WindowsSessionInfo(
                    (uint)item.SessionId,
                    ToSessionState(item.State),
                    item.WinStationName,
                    QuerySid(item.SessionId),
                    QueryString(item.SessionId, WtsInfoUserName),
                    QueryString(item.SessionId, WtsInfoDomainName));
            }

            return sessions;
        }
        finally
        {
            WTSFreeMemory(sessionInfo);
        }
    }

    private static string QueryString(int sessionId, int infoClass)
    {
        if (!WTSQuerySessionInformation(
                IntPtr.Zero,
                sessionId,
                infoClass,
                out var buffer,
                out _))
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static string QuerySid(int sessionId)
    {
        if (!WTSQuerySessionInformation(
                IntPtr.Zero,
                sessionId,
                WtsInfoUserSid,
                out var buffer,
                out _))
        {
            return string.Empty;
        }

        try
        {
            var stringSid = Marshal.PtrToStringUni(buffer);
            if (string.IsNullOrWhiteSpace(stringSid))
            {
                return string.Empty;
            }

            var sid = new SecurityIdentifier(stringSid);
            return sid.Value;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static WindowsSessionState ToSessionState(int state)
    {
        return state switch
        {
            0 => WindowsSessionState.Active,
            1 => WindowsSessionState.Connected,
            2 => WindowsSessionState.ConnectQuery,
            3 => WindowsSessionState.Shadow,
            4 => WindowsSessionState.Disconnected,
            5 => WindowsSessionState.Idle,
            6 => WindowsSessionState.Listen,
            7 => WindowsSessionState.Reset,
            8 => WindowsSessionState.Down,
            9 => WindowsSessionState.Init,
            _ => WindowsSessionState.Unknown
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string WinStationName;

        public int State;
    }
}
