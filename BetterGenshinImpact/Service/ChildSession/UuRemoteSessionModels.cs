using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Service.ChildSession;

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

internal sealed record WindowsSessionInfo(
    uint SessionId,
    WindowsSessionState State,
    string WinStationName,
    string UserSid,
    string UserName,
    string DomainName)
{
    public bool IsActive => State == WindowsSessionState.Active;

    public bool IsConsole => string.Equals(
        WinStationName,
        "Console",
        StringComparison.OrdinalIgnoreCase);
}

internal sealed record GameViewerServerInfo(int ProcessId, int SessionId);

internal sealed record UuRemoteSessionSnapshot(
    uint? ConsoleSessionId,
    int CurrentSessionId,
    string CurrentUserSid,
    string CurrentUserAccountName,
    IReadOnlyList<WindowsSessionInfo> Sessions,
    IReadOnlyList<GameViewerServerInfo> GameViewerServers);

internal sealed record UuRemoteSessionAssessment(
    bool HasUuStreamingProcess,
    bool HasConflict,
    bool HasOtherUserActiveSession,
    IReadOnlyList<int> GameViewerSessions,
    IReadOnlyList<uint> SessionsToDisconnect,
    string? BlockingReason)
{
    public bool CanRepair => HasConflict && !HasOtherUserActiveSession
                                      && string.IsNullOrWhiteSpace(BlockingReason);
}

internal static class UuRemoteSessionEvaluator
{
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
        var hasOtherUserActiveSession = activeBackgroundSessions.Any(
            x => !IsCurrentUser(snapshot, x));
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

public enum UuRemoteRepairState
{
    Idle,
    Checking,
    Repairing,
    Succeeded,
    Failed,
    PostStartConflict
}

public sealed class UuRemoteRepairStateChangedEventArgs(
    UuRemoteRepairState state,
    string message) : EventArgs
{
    public UuRemoteRepairState State { get; } = state;

    public string Message { get; } = message;
}
