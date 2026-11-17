using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.ChildSession;

/// <summary>WTS 会话连接状态。</summary>
internal enum WindowsSessionState
{
    Active,
    Connected,
    ConnectQuery,
    Shadow,
    Disconnected,
    Idle,
    Listen,
    Reset,
    Down,
    Init,
    Unknown
}

/// <summary>会话状态与所有者身份，用于限定可断开的当前用户后台会话。</summary>
internal sealed record WindowsSessionInfo(
    uint SessionId,
    WindowsSessionState State,
    string WinStationName,
    string UserSid,
    string UserName,
    string DomainName)
{
    /// <summary>仅活动会话允许提交断开请求。</summary>
    public bool IsActive => State == WindowsSessionState.Active;

    /// <summary>额外保护控制台窗口站，不只依赖可能正切换的 Console Session ID。</summary>
    public bool IsConsole => string.Equals(
        WinStationName,
        "Console",
        StringComparison.OrdinalIgnoreCase);
}

/// <summary>UU 串流进程的轻量快照，不持有 Process 句柄。</summary>
internal sealed record GameViewerServerInfo(int ProcessId, int SessionId);

/// <summary>一次检测中的控制台、当前用户及 UU 进程会话信息。</summary>
internal sealed record UuRemoteSessionSnapshot(
    uint? ConsoleSessionId,
    int CurrentSessionId,
    string CurrentUserSid,
    string CurrentUserAccountName,
    IReadOnlyList<WindowsSessionInfo> Sessions,
    IReadOnlyList<GameViewerServerInfo> GameViewerServers);

/// <summary>纯逻辑判定结果与安全断开候选列表。</summary>
internal sealed record UuRemoteSessionAssessment(
    bool HasUuStreamingProcess,
    bool HasConflict,
    bool HasOtherUserActiveSession,
    IReadOnlyList<int> GameViewerSessions,
    IReadOnlyList<uint> SessionsToDisconnect,
    string? BlockingReason)
{
    /// <summary>有冲突且未发现其他用户或未知所有者的活动会话。</summary>
    public bool CanRepair => HasConflict && !HasOtherUserActiveSession
                                      && string.IsNullOrWhiteSpace(BlockingReason);
}

/// <summary>只基于快照作出判定，便于覆盖多用户与多进程场景。</summary>
internal static class UuRemoteSessionEvaluator
{
    /// <summary>将非控制台 UU 串流进程判为冲突，过滤当前用户的活动后台会话。</summary>
    internal static UuRemoteSessionAssessment Evaluate(UuRemoteSessionSnapshot snapshot)
    {
        var gameViewerSessions = snapshot.GameViewerServers
            .Select(x => x.SessionId)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (snapshot.ConsoleSessionId is null)
        {
            return new UuRemoteSessionAssessment(
                gameViewerSessions.Length > 0,
                false,
                false,
                gameViewerSessions,
                [],
                "无法确定当前的主桌面会话。");
        }

        var consoleSessionId = snapshot.ConsoleSessionId.Value;
        var activeBackgroundSessions = snapshot.Sessions
            .Where(x =>
                x.IsActive
                && x.SessionId != 0
                && x.SessionId != consoleSessionId
                && !x.IsConsole)
            .ToArray();
        var conflictingProcesses = snapshot.GameViewerServers
            .Where(x => x.SessionId != consoleSessionId)
            .OrderBy(x => x.ProcessId)
            .ToArray();
        var conflictSessions = conflictingProcesses
            .Select(x => x.SessionId)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        var sessionsToDisconnect = activeBackgroundSessions
            .Where(x => IsCurrentUser(snapshot, x))
            .Select(x => x.SessionId)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        // 保护所有其他用户的活动会话，包括切换期间仍标为 Console 的窗口站。
        var hasOtherUserActiveSession = snapshot.Sessions.Any(
            x => x.IsActive && x.SessionId != 0 && !IsCurrentUser(snapshot, x));
        var blockingReason = hasOtherUserActiveSession
            ? "检测到其他 Windows 用户或无法确认所有者的活动会话，已取消修复以避免影响其他用户。"
            : null;

        return new UuRemoteSessionAssessment(
            gameViewerSessions.Length > 0,
            conflictSessions.Length > 0,
            hasOtherUserActiveSession,
            conflictSessions,
            blockingReason is null ? sessionsToDisconnect : [],
            blockingReason);
    }

    /// <summary>SID 优先；SID 不可用时只允许完整账户名的明确匹配。</summary>
    internal static bool IsCurrentUser(
        UuRemoteSessionSnapshot snapshot,
        WindowsSessionInfo session)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.CurrentUserSid)
            && !string.IsNullOrWhiteSpace(session.UserSid))
        {
            return string.Equals(
                snapshot.CurrentUserSid,
                session.UserSid,
                StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(snapshot.CurrentUserAccountName)
            || string.IsNullOrWhiteSpace(session.UserName))
        {
            return false;
        }

        var accountName = string.IsNullOrWhiteSpace(session.DomainName)
            ? session.UserName
            : $"{session.DomainName}\\{session.UserName}";
        return string.Equals(
            snapshot.CurrentUserAccountName,
            accountName,
            StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>向桌面分身窗口报告的 UU 检查和修复阶段。</summary>
public enum UuRemoteRepairState
{
    Idle,
    Checking,
    Repairing,
    Succeeded,
    Failed,
    PostStartConflict
}

/// <summary>UI 提示与修复前呈现确认。</summary>
public sealed class UuRemoteRepairStateChangedEventArgs(
    UuRemoteRepairState state,
    string message) : EventArgs
{
    /// <summary>当前检查或修复阶段。</summary>
    public UuRemoteRepairState State { get; } = state;

    /// <summary>供日志及窗口展示的中文说明。</summary>
    public string Message { get; } = message;

    /// <summary>UI 订阅方在风险提示完成渲染后结束；修复服务等待它再进行会话操作。</summary>
    internal Task PresentationCompleted { get; set; } = Task.CompletedTask;
}

/// <summary>一次修复结果；即使只完成部分断开，也要让调用方恢复 RDP 连接。</summary>
internal sealed record UuRemoteSessionRepairResult(
    bool WasAttempted,
    bool IsSuccessful,
    IReadOnlyList<int> OriginalSessions,
    IReadOnlyList<int> RepairedSessions,
    string? FailureReason)
{
    /// <summary>是否已请求断开当前用户会话，不能仅依赖可能滞后的 ActiveX 连接状态。</summary>
    internal bool DisconnectedUserSessions { get; init; }

    /// <summary>未发现冲突或本周期已经消耗过修复机会。</summary>
    internal static UuRemoteSessionRepairResult NotNeeded(string? reason = null)
        => new(false, true, [], [], reason);

    /// <summary>修复后 UU 串流进程已返回控制台。</summary>
    internal static UuRemoteSessionRepairResult Successful(
        IReadOnlyList<int> originalSessions, IReadOnlyList<int> repairedSessions)
        => new(true, true, originalSessions, repairedSessions, null);

    /// <summary>安全检查、会话操作或进程重启未完成。</summary>
    internal static UuRemoteSessionRepairResult Failed(IReadOnlyList<int> originalSessions, string reason)
        => new(true, false, originalSessions, [], reason);
}
