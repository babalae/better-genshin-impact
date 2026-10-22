using BetterGenshinImpact.Service.ChildSession;

namespace BetterGenshinImpact.UnitTest;

public class UuRemoteSessionEvaluatorTest
{
    private const string CurrentUser = "DESKTOP\\alice";
    private const string CurrentUserSid = "S-1-5-21-alice";

    [Fact]
    public void Evaluate_NoStreamingProcess_DoesNotConflict()
    {
        var assessment = UuRemoteSessionEvaluator.Evaluate(
            CreateSnapshot([], [CreateSession(1, "alice")]));

        Assert.False(assessment.HasUuStreamingProcess);
        Assert.False(assessment.HasConflict);
        Assert.False(assessment.CanRepair);
    }

    [Fact]
    public void Evaluate_StreamingProcessInConsoleSession_DoesNotConflict()
    {
        var assessment = UuRemoteSessionEvaluator.Evaluate(
            CreateSnapshot(
                [new GameViewerServerInfo(10, 1)],
                [CreateSession(1, "alice")]));

        Assert.True(assessment.HasUuStreamingProcess);
        Assert.False(assessment.HasConflict);
    }

    [Fact]
    public void Evaluate_StreamingProcessInNonConsoleSession_RequestsRepair()
    {
        var assessment = UuRemoteSessionEvaluator.Evaluate(
            CreateSnapshot(
                [new GameViewerServerInfo(10, 3)],
                [
                    CreateSession(1, "alice"),
                    CreateSession(3, "alice")
                ]));

        Assert.True(assessment.HasConflict);
        Assert.True(assessment.CanRepair);
        Assert.Equal(new[] { 3 }, assessment.GameViewerSessions);
        Assert.Equal(new uint[] { 3 }, assessment.SessionsToDisconnect);
    }

    [Fact]
    public void Evaluate_MultipleStreamingProcesses_ReportsAllConflictSessions()
    {
        var assessment = UuRemoteSessionEvaluator.Evaluate(
            CreateSnapshot(
                [
                    new GameViewerServerInfo(10, 2),
                    new GameViewerServerInfo(11, 3),
                    new GameViewerServerInfo(12, 1)
                ],
                [
                    CreateSession(1, "alice"),
                    CreateSession(2, "alice"),
                    CreateSession(3, "alice")
                ]));

        Assert.True(assessment.HasConflict);
        Assert.Equal(new[] { 2, 3 }, assessment.GameViewerSessions);
    }

    [Fact]
    public void Evaluate_NoConsoleSession_CannotRepair()
    {
        var snapshot = new UuRemoteSessionSnapshot(
            null,
            1,
            CurrentUserSid,
            CurrentUser,
            [CreateSession(1, "alice")],
            [new GameViewerServerInfo(10, 3)]);

        var assessment = UuRemoteSessionEvaluator.Evaluate(snapshot);

        Assert.False(assessment.HasConflict);
        Assert.False(assessment.CanRepair);
        Assert.Empty(assessment.SessionsToDisconnect);
        Assert.Contains("主桌面", assessment.BlockingReason);
    }

    [Fact]
    public void Evaluate_OtherUserActiveSession_BlocksRepair()
    {
        var assessment = UuRemoteSessionEvaluator.Evaluate(
            CreateSnapshot(
                [new GameViewerServerInfo(10, 3)],
                [
                    CreateSession(1, "alice"),
                    CreateSession(2, "bob"),
                    CreateSession(3, "alice")
                ]));

        Assert.True(assessment.HasConflict);
        Assert.True(assessment.HasOtherUserActiveSession);
        Assert.False(assessment.CanRepair);
        Assert.Empty(assessment.SessionsToDisconnect);
        Assert.Contains("其他 Windows 用户", assessment.BlockingReason);
    }

    [Fact]
    public void Evaluate_OtherUserDisconnectedSession_DoesNotBlockRepair()
    {
        var assessment = UuRemoteSessionEvaluator.Evaluate(
            CreateSnapshot(
                [new GameViewerServerInfo(10, 3)],
                [
                    CreateSession(1, "alice"),
                    CreateSession(3, "alice"),
                    CreateSession(4, "bob", WindowsSessionState.Disconnected)
                ]));

        Assert.True(assessment.CanRepair);
        Assert.Null(assessment.BlockingReason);
        Assert.Equal(new uint[] { 3 }, assessment.SessionsToDisconnect);
    }

    [Fact]
    public void Evaluate_CurrentUserDisconnectedSession_IsNotDisconnectedAgain()
    {
        var assessment = UuRemoteSessionEvaluator.Evaluate(
            CreateSnapshot(
                [new GameViewerServerInfo(10, 3)],
                [
                    CreateSession(1, "alice"),
                    CreateSession(3, "alice"),
                    CreateSession(4, "alice", WindowsSessionState.Disconnected)
                ]));

        Assert.True(assessment.CanRepair);
        Assert.Equal(new uint[] { 3 }, assessment.SessionsToDisconnect);
    }

    [Fact]
    public void Evaluate_ConsoleWinStationOutsideActiveConsoleId_IsNotDisconnected()
    {
        var assessment = UuRemoteSessionEvaluator.Evaluate(
            CreateSnapshot(
                [new GameViewerServerInfo(10, 3)],
                [
                    CreateSession(1, "alice"),
                    new WindowsSessionInfo(
                        3,
                        WindowsSessionState.Active,
                        "Console",
                        CurrentUserSid,
                        "alice",
                        "DESKTOP")
                ]));

        Assert.True(assessment.CanRepair);
        Assert.Empty(assessment.SessionsToDisconnect);
    }

    [Fact]
    public void Evaluate_CurrentUserMatchedBySid_RequestsDisconnect()
    {
        var assessment = UuRemoteSessionEvaluator.Evaluate(
            CreateSnapshot(
                [new GameViewerServerInfo(10, 3)],
                [
                    CreateSession(1, "alice", sid: CurrentUserSid),
                    CreateSession(3, "renamed-user", sid: CurrentUserSid)
                ]));

        Assert.True(assessment.CanRepair);
        Assert.Equal(new uint[] { 3 }, assessment.SessionsToDisconnect);
    }

    [Fact]
    public void Evaluate_SessionWithoutOwner_BlocksRepair()
    {
        var assessment = UuRemoteSessionEvaluator.Evaluate(
            CreateSnapshot(
                [new GameViewerServerInfo(10, 3)],
                [
                    CreateSession(1, "alice"),
                    CreateSession(3, "alice"),
                    CreateSession(5, string.Empty, sid: string.Empty)
                ]));

        Assert.True(assessment.HasOtherUserActiveSession);
        Assert.False(assessment.CanRepair);
        Assert.Empty(assessment.SessionsToDisconnect);
    }

    private static UuRemoteSessionSnapshot CreateSnapshot(
        IReadOnlyList<GameViewerServerInfo> processes,
        IReadOnlyList<WindowsSessionInfo> sessions)
    {
        return new UuRemoteSessionSnapshot(
            1,
            1,
            CurrentUserSid,
            CurrentUser,
            sessions,
            processes);
    }

    private static WindowsSessionInfo CreateSession(
        uint sessionId,
        string userName,
        WindowsSessionState state = WindowsSessionState.Active,
        string? sid = null)
    {
        return new WindowsSessionInfo(
            sessionId,
            state,
            sessionId == 1 ? "Console" : $"RDP-Tcp#{sessionId}",
            sid ?? $"S-1-5-21-{userName}",
            userName,
            "DESKTOP");
    }
}
