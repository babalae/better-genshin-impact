using System.Collections.Concurrent;
using System.Diagnostics;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.ChildSession;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BetterGenshinImpact.UnitTest;

/// <summary>通过假平台验证修复流程，不断开真实会话或停止真实 UU 进程。</summary>
public class UuRemoteSessionRepairServiceTest
{
    /// <summary>新配置必须保持自动修复与风险确认均默认关闭。</summary>
    [Fact]
    public void Config_DefaultsToDisabledAndUnacknowledged()
    {
        var config = new ChildSessionConfig();
        Assert.False(config.AutoRepairUuRemoteSession);
        Assert.False(config.UuRemoteRepairRiskAcknowledged);
    }

    /// <summary>关闭开关时不得查询会话或操作进程。</summary>
    [Fact]
    public async Task Disabled_DoesNotInspectOrRepair()
    {
        var context = new RepairTestContext();
        context.Config.AutoRepairUuRemoteSession = false;
        var result = await context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        Assert.False(result.WasAttempted);
        Assert.Equal(0, context.Platform.CaptureCount);
        Assert.Equal(0, context.Platform.StopCount);
    }

    /// <summary>无冲突时不重启 UU，后台查询不能回到 UI 线程。</summary>
    [Fact]
    public async Task NoConflict_DoesNotRestartStreamingProcess()
    {
        var context = new RepairTestContext();
        context.Platform.Snapshot = CreateSnapshot(processes: [new GameViewerServerInfo(10, 1)]);
        var result = await context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        Assert.False(result.WasAttempted);
        Assert.Empty(context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
        Assert.All(context.Platform.CaptureOnThreadPool, Assert.True);
    }

    /// <summary>检查后 UU 自行退出时，应等待进程回到控制台，不能把无进程误报为修复成功。</summary>
    [Fact]
    public async Task StreamingProcessDisappearsBeforeRepair_DoesNotReportFalseSuccess()
    {
        var context = new RepairTestContext();
        context.Platform.SnapshotFactory = count => CreateSnapshot(
            sessions: [CreateSession(1), CreateSession(3, state: WindowsSessionState.Disconnected)],
            processes: count == 1 ? [new GameViewerServerInfo(10, 3)] : []);
        var result = await context.RunAsync(context.Service.TryRepairBeforeStartAsync(CancellationToken.None));
        Assert.False(result.IsSuccessful);
        Assert.Contains("当前未检测到 GameViewerServer", result.FailureReason);
        Assert.Equal(0, context.Platform.StopCount);
    }

    /// <summary>根实例、管理员权限及控制台位置都必须满足。</summary>
    [Theory]
    [InlineData(false, true, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 3)]
    public async Task UnsafeInstance_DoesNotTouchSessions(bool isRoot, bool isElevated, int currentSession)
    {
        var context = new RepairTestContext();
        context.Platform.IsRoot = isRoot;
        context.Platform.IsElevated = isElevated;
        context.Platform.Snapshot = CreateSnapshot(currentSessionId: currentSession);
        var result = await context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        Assert.False(result.IsSuccessful);
        Assert.Empty(context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
    }

    /// <summary>没有待断开会话时，也要在停止进程前复查其他用户。</summary>
    [Fact]
    public async Task OtherUserAppearsBeforeKill_BlocksRepairWithoutDisconnectCandidates()
    {
        var context = new RepairTestContext();
        var original = CreateSnapshot(sessions: [CreateSession(1), CreateSession(3, state: WindowsSessionState.Disconnected)]);
        context.Platform.SnapshotFactory = count => count < 3 ? original : original with
        {
            Sessions = [CreateSession(1), CreateSession(3, state: WindowsSessionState.Disconnected), CreateSession(4, "bob")]
        };
        var result = await context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        Assert.False(result.IsSuccessful);
        Assert.Contains("其他 Windows 用户", result.FailureReason);
        Assert.Empty(context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
    }

    /// <summary>首次快照之后切换控制台，不能继续使用旧 Console Session ID。</summary>
    [Fact]
    public async Task ConsoleChangesBeforeRepair_BlocksRepair()
    {
        var context = new RepairTestContext();
        context.Platform.SnapshotFactory = count => CreateSnapshot(consoleSessionId: count == 1 ? 1u : 4u);
        var result = await context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        Assert.False(result.IsSuccessful);
        Assert.Contains("主桌面", result.FailureReason);
        Assert.Empty(context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
    }

    /// <summary>不能把异步排队通知当成用户已经看到风险说明。</summary>
    [Fact]
    public async Task RiskPresentation_CompletesBeforeAnySessionOperation()
    {
        var context = new RepairTestContext();
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Service.RepairStateChanged += (_, e) =>
        {
            if (e.State == UuRemoteRepairState.Repairing)
            {
                e.PresentationCompleted = presented.Task;
                notified.TrySetResult();
            }
        };
        var task = context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(task.IsCompleted);
        Assert.Empty(context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
        presented.TrySetResult();
        Assert.True((await context.RunAsync(task)).IsSuccessful);
    }

    /// <summary>等待 UI 时注销，必须取消后续断开和停止操作并释放修复门。</summary>
    [Fact]
    public async Task CancelWhilePresentingRisk_DoesNotTouchSessionsOrProcesses()
    {
        var context = new RepairTestContext();
        using var cancellation = new CancellationTokenSource();
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presented = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Service.RepairStateChanged += (_, e) =>
        {
            if (e.State == UuRemoteRepairState.Repairing)
            {
                e.PresentationCompleted = presented.Task;
                notified.TrySetResult();
            }
        };
        var task = context.Service.TryRepairBeforeStartAsync(cancellation.Token);
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
        Assert.False(context.Service.IsRepairing);

        presented.TrySetResult();
        context.Service.BeginStartupCycle();
        Assert.True((await context.RunAsync(context.Service.TryRepairBeforeStartAsync(CancellationToken.None))).IsSuccessful);
    }

    /// <summary>无法呈现风险提示时返回修复失败，不能继续断开会话或让调用方启动流程抛异常。</summary>
    [Fact]
    public async Task RiskPresentationFails_ReturnsFailureWithoutSessionOperations()
    {
        var context = new RepairTestContext();
        context.Service.RepairStateChanged += (_, e) =>
        {
            if (e.State == UuRemoteRepairState.Repairing)
            {
                e.PresentationCompleted = Task.FromException(new InvalidOperationException("窗口正在关闭"));
            }
        };
        var result = await context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        Assert.False(result.IsSuccessful);
        Assert.Contains("窗口正在关闭", result.FailureReason);
        Assert.Empty(context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
        Assert.False(context.Service.IsRepairing);
    }

    /// <summary>WTS 查询失败必须返回失败结果，让调用方仍能继续连接桌面分身。</summary>
    [Fact]
    public async Task InspectionFails_ReturnsFailureInsteadOfThrowing()
    {
        var context = new RepairTestContext();
        context.Platform.SnapshotFactory = _ => throw new InvalidOperationException("无法枚举 Windows 会话");
        var result = await context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        Assert.False(result.IsSuccessful);
        Assert.Contains("无法枚举 Windows 会话", result.FailureReason);
        Assert.Empty(context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
        Assert.False(context.Service.IsRepairing);
    }

    /// <summary>一次会话断开后立即取消，不能继续断开其余会话或重启 UU。</summary>
    [Fact]
    public async Task CancelAfterFirstDisconnect_StopsRemainingOperations()
    {
        var context = new RepairTestContext();
        using var cancellation = new CancellationTokenSource();
        context.Platform.Snapshot = CreateSnapshot(sessions: [CreateSession(1), CreateSession(3), CreateSession(4)]);
        context.Platform.OnDisconnect = _ => cancellation.Cancel();
        var task = context.Service.TryRepairBeforeStartAsync(cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(new uint[] { 3 }, context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
    }

    /// <summary>呈现提示期间关闭开关，应在任何破坏性操作前停止修复。</summary>
    [Fact]
    public async Task DisabledDuringRiskPresentation_DoesNotTouchSessions()
    {
        var context = new RepairTestContext();
        context.Service.RepairStateChanged += (_, e) =>
        {
            if (e.State == UuRemoteRepairState.Repairing)
            {
                context.Service.SetEnabled(false);
            }
        };
        var result = await context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        Assert.False(result.IsSuccessful);
        Assert.Contains("已关闭", result.FailureReason);
        Assert.Empty(context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
    }

    /// <summary>部分断开后失败，也必须要求调用方恢复 RDP。</summary>
    [Fact]
    public async Task PartialDisconnectFailure_PreservesReconnectRequirement()
    {
        var context = new RepairTestContext();
        context.Platform.Snapshot = CreateSnapshot(sessions: [CreateSession(1), CreateSession(3), CreateSession(4)]);
        context.Platform.FailDisconnectFor = 4;
        var result = await context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        Assert.False(result.IsSuccessful);
        Assert.True(result.DisconnectedUserSessions);
        Assert.Equal(new uint[] { 3, 4 }, context.Platform.DisconnectCalls);
        Assert.Equal(0, context.Platform.StopCount);
    }

    /// <summary>只断开当前用户活动后台会话，并确认全部串流进程已回到控制台。</summary>
    [Fact]
    public async Task SuccessfulRepair_ReportsConsoleSessionsAndReconnectRequirement()
    {
        var context = new RepairTestContext();
        context.Platform.Snapshot = CreateSnapshot(
            sessions: [CreateSession(1), CreateSession(3), CreateSession(4), CreateSession(5, "bob", WindowsSessionState.Disconnected)],
            processes: [new GameViewerServerInfo(10, 3), new GameViewerServerInfo(11, 4), new GameViewerServerInfo(12, 1)]);
        var result = await context.RunAsync(context.Service.TryRepairBeforeStartAsync(CancellationToken.None));
        Assert.True(result.IsSuccessful);
        Assert.True(result.DisconnectedUserSessions);
        Assert.Equal(new uint[] { 3, 4 }, context.Platform.DisconnectCalls);
        Assert.Equal(new[] { 3, 4 }, result.OriginalSessions);
        Assert.Equal(new[] { 1 }, result.RepairedSessions);
        Assert.Equal(1, context.Platform.StopCount);
        Assert.Equal(new[] { 10, 11 }, context.Platform.StoppedProcessIds);
    }

    /// <summary>停止多个进程之间有其他用户上线时，不能继续停止剩余进程。</summary>
    [Fact]
    public async Task OtherUserAppearsBetweenProcessStops_BlocksRemainingStops()
    {
        var context = new RepairTestContext();
        context.Platform.Snapshot = CreateSnapshot(processes: [new GameViewerServerInfo(10, 3), new GameViewerServerInfo(11, 3)]);
        context.Platform.OnStoppedProcess = _ => context.Platform.Snapshot = context.Platform.Snapshot with
        {
            Sessions = [CreateSession(1), CreateSession(3, state: WindowsSessionState.Disconnected), CreateSession(4, "bob")]
        };
        var result = await context.RunAsync(context.Service.TryRepairBeforeStartAsync(CancellationToken.None));
        Assert.False(result.IsSuccessful);
        Assert.True(result.DisconnectedUserSessions);
        Assert.Contains("其他 Windows 用户", result.FailureReason);
        Assert.Equal(new[] { 10 }, context.Platform.StoppedProcessIds);
    }

    /// <summary>停止多个进程之间关闭开关时，不能继续执行其余停止操作。</summary>
    [Fact]
    public async Task DisabledBetweenProcessStops_BlocksRemainingStops()
    {
        var context = new RepairTestContext();
        context.Platform.Snapshot = CreateSnapshot(processes: [new GameViewerServerInfo(10, 3), new GameViewerServerInfo(11, 3)]);
        context.Platform.OnStoppedProcess = _ => context.Service.SetEnabled(false);
        var result = await context.RunAsync(context.Service.TryRepairBeforeStartAsync(CancellationToken.None));
        Assert.False(result.IsSuccessful);
        Assert.Contains("已关闭", result.FailureReason);
        Assert.Equal(new[] { 10 }, context.Platform.StoppedProcessIds);
    }

    /// <summary>停止期间新建当前用户的活动后台会话时，也必须中止其余进程停止操作。</summary>
    [Fact]
    public async Task NewActiveBackgroundSessionBetweenProcessStops_BlocksRemainingStops()
    {
        var context = new RepairTestContext();
        context.Platform.Snapshot = CreateSnapshot(processes: [new GameViewerServerInfo(10, 3), new GameViewerServerInfo(11, 3)]);
        context.Platform.OnStoppedProcess = _ => context.Platform.Snapshot = context.Platform.Snapshot with
        {
            Sessions = [CreateSession(1), CreateSession(3, state: WindowsSessionState.Disconnected), CreateSession(4)]
        };
        var result = await context.RunAsync(context.Service.TryRepairBeforeStartAsync(CancellationToken.None));
        Assert.False(result.IsSuccessful);
        Assert.Contains("新的活动后台", result.FailureReason);
        Assert.Equal(new[] { 10 }, context.Platform.StoppedProcessIds);
    }

    /// <summary>WTS 异步断开未完成时有界失败，不能重启仍处于活动后台会话中的 UU。</summary>
    [Fact]
    public async Task DisconnectNeverCompletes_DoesNotStopStreamingProcess()
    {
        var context = new RepairTestContext();
        context.Platform.KeepSessionsActive = true;
        var result = await context.RunAsync(context.Service.TryRepairBeforeStartAsync(CancellationToken.None));
        Assert.False(result.IsSuccessful);
        Assert.True(result.DisconnectedUserSessions);
        Assert.Contains("尚未完成断开", result.FailureReason);
        Assert.Equal(0, context.Platform.StopCount);
    }

    /// <summary>UU 不重启时在超时后返回，后续冲突也不得重复停止进程。</summary>
    [Fact]
    public async Task StreamingRestartTimeout_DoesNotRetryInSameCycle()
    {
        var context = new RepairTestContext();
        context.Platform.RestartInConsole = false;
        var result = await context.RunAsync(context.Service.TryRepairBeforeStartAsync(CancellationToken.None));
        Assert.False(result.IsSuccessful);
        Assert.Contains("20 秒", result.FailureReason);
        Assert.Equal(1, context.Platform.StopCount);
        context.Platform.Snapshot = CreateSnapshot();
        var states = new ConcurrentQueue<UuRemoteRepairState>();
        context.Service.RepairStateChanged += (_, e) => states.Enqueue(e.State);
        var repeated = await context.Service.TryRepairBeforeStartAsync(CancellationToken.None);
        Assert.False(repeated.WasAttempted);
        Assert.Contains(UuRemoteRepairState.PostStartConflict, states);
        Assert.Equal(1, context.Platform.StopCount);
    }

    /// <summary>关闭开关后，登录后的观察应停止，不再检查或提示冲突。</summary>
    [Fact]
    public async Task DisabledPostStartMonitor_DoesNotInspect()
    {
        var context = new RepairTestContext();
        var task = context.Service.CheckAfterStartAsync(CancellationToken.None);
        context.Service.SetEnabled(false);
        var result = await context.RunAsync(task);
        Assert.False(result.WasAttempted);
        Assert.Equal(0, context.Platform.CaptureCount);
    }

    /// <summary>启动前没有冲突时，允许登录后观察执行本周期的首次修复。</summary>
    [Fact]
    public async Task FirstConflictAfterLogin_RepairsOnce()
    {
        var context = new RepairTestContext();
        context.Platform.Snapshot = CreateSnapshot(processes: []);
        Assert.False((await context.Service.TryRepairBeforeStartAsync(CancellationToken.None)).WasAttempted);
        context.Platform.Snapshot = CreateSnapshot();
        var result = await context.RunAsync(context.Service.CheckAfterStartAsync(CancellationToken.None));
        Assert.True(result.IsSuccessful);
        Assert.True(result.WasAttempted);
        Assert.Equal(1, context.Platform.StopCount);
    }

    /// <summary>创建控制台用户与后台串流进程的快照。</summary>
    private static UuRemoteSessionSnapshot CreateSnapshot(
        IReadOnlyList<WindowsSessionInfo>? sessions = null,
        IReadOnlyList<GameViewerServerInfo>? processes = null,
        uint? consoleSessionId = 1,
        int currentSessionId = 1)
        => new(consoleSessionId, currentSessionId, "S-1-5-21-1-2-3-1001", "DESKTOP\\alice",
            sessions ?? [CreateSession(1), CreateSession(3)],
            processes ?? [new GameViewerServerInfo(10, 3)]);

    /// <summary>创建一个已知用户的 WTS 会话。</summary>
    private static WindowsSessionInfo CreateSession(uint id, string user = "alice",
        WindowsSessionState state = WindowsSessionState.Active)
        => new(id, state, id == 1 ? "Console" : $"RDP-Tcp#{id}",
            user == "alice" ? "S-1-5-21-1-2-3-1001" : "S-1-5-21-1-2-3-1002", user, "DESKTOP");

    /// <summary>线程安全的会话与进程替身，记录所有破坏性操作。</summary>
    private sealed class FakePlatform : IUuRemoteSessionPlatform
    {
        /// <summary>不可变快照通过原子读写跨线程共享。</summary>
        private UuRemoteSessionSnapshot _snapshot = CreateSnapshot();
        /// <summary>快照读取次数。</summary>
        private int _captureCount;
        /// <summary>停止串流进程次数。</summary>
        private int _stopCount;

        /// <inheritdoc />
        public bool IsRoot { get; set; } = true;
        /// <inheritdoc />
        public bool IsElevated { get; set; } = true;
        /// <inheritdoc />
        public int CurrentSessionId => Snapshot.CurrentSessionId;
        /// <inheritdoc />
        public uint? ConsoleSessionId => Snapshot.ConsoleSessionId;
        /// <summary>当前模拟的系统状态。</summary>
        internal UuRemoteSessionSnapshot Snapshot
        {
            get => Volatile.Read(ref _snapshot);
            set => Volatile.Write(ref _snapshot, value);
        }
        /// <summary>模拟检查与操作间的会话变化。</summary>
        internal Func<int, UuRemoteSessionSnapshot>? SnapshotFactory { get; set; }
        /// <summary>记录断开目标。</summary>
        internal ConcurrentQueue<uint> DisconnectCalls { get; } = new();
        /// <summary>记录停止的串流进程，控制台进程不应被停止。</summary>
        internal ConcurrentQueue<int> StoppedProcessIds { get; } = new();
        /// <summary>记录查询是否位于线程池，避免退回 UI 线程。</summary>
        internal ConcurrentQueue<bool> CaptureOnThreadPool { get; } = new();
        /// <summary>模拟指定会话的断开失败。</summary>
        internal uint? FailDisconnectFor { get; set; }
        /// <summary>模拟异步断开请求始终未生效。</summary>
        internal bool KeepSessionsActive { get; set; }
        /// <summary>模拟 UU 服务未重新拉起进程。</summary>
        internal bool RestartInConsole { get; set; } = true;
        /// <summary>在断开一次会话后注入取消或其他状态变化。</summary>
        internal Action<uint>? OnDisconnect { get; set; }
        /// <summary>在停止一个进程后注入会话或开关变化。</summary>
        internal Action<int>? OnStoppedProcess { get; set; }
        /// <summary>快照读取次数。</summary>
        internal int CaptureCount => Volatile.Read(ref _captureCount);
        /// <summary>停止串流进程次数。</summary>
        internal int StopCount => Volatile.Read(ref _stopCount);

        /// <inheritdoc />
        public UuRemoteSessionSnapshot CaptureSnapshot()
        {
            CaptureOnThreadPool.Enqueue(Thread.CurrentThread.IsThreadPoolThread);
            var count = Interlocked.Increment(ref _captureCount);
            if (SnapshotFactory is not null)
            {
                Snapshot = SnapshotFactory(count);
            }
            return Snapshot;
        }

        /// <inheritdoc />
        public bool DisconnectSession(uint sessionId, out int errorCode)
        {
            DisconnectCalls.Enqueue(sessionId);
            errorCode = FailDisconnectFor == sessionId ? 5 : 0;
            if (errorCode != 0)
            {
                return false;
            }
            if (!KeepSessionsActive)
            {
                Snapshot = Snapshot with
                {
                    Sessions = Snapshot.Sessions.Select(x => x.SessionId == sessionId
                        ? x with { State = WindowsSessionState.Disconnected } : x).ToArray()
                };
            }
            OnDisconnect?.Invoke(sessionId);
            return true;
        }

        /// <inheritdoc />
        public async Task StopStreamingProcessesAsync(uint consoleSessionId,
            Func<CancellationToken, Task> validateBeforeStopAsync, CancellationToken cancellationToken)
        {
            var streamingProcesses = Snapshot.GameViewerServers.Where(x => x.SessionId != consoleSessionId).ToArray();
            var stoppedAny = false;
            foreach (var process in streamingProcesses)
            {
                await validateBeforeStopAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!stoppedAny)
                {
                    Interlocked.Increment(ref _stopCount);
                    stoppedAny = true;
                }
                StoppedProcessIds.Enqueue(process.ProcessId);
                Snapshot = Snapshot with
                {
                    GameViewerServers = Snapshot.GameViewerServers.Where(x => x.ProcessId != process.ProcessId).ToArray()
                };
                OnStoppedProcess?.Invoke(process.ProcessId);
            }
            Snapshot = Snapshot with
            {
                GameViewerServers = stoppedAny && RestartInConsole
                    ? [new GameViewerServerInfo(20, (int)consoleSessionId)] : Snapshot.GameViewerServers
            };
        }

        /// <inheritdoc />
        public IReadOnlyList<GameViewerServerInfo> GetStreamingProcesses() => Snapshot.GameViewerServers;
    }

    /// <summary>测试专用时钟及服务宿主。</summary>
    private sealed class RepairTestContext
    {
        /// <summary>默认已开启开关，便于覆盖修复主流程。</summary>
        internal ChildSessionConfig Config { get; } = new() { AutoRepairUuRemoteSession = true };
        /// <summary>加速有界等待，不让单测真实等待 20 或 60 秒。</summary>
        internal FakeTimeProvider Time { get; } = new();
        /// <summary>不接触真实 WTS 的平台替身。</summary>
        internal FakePlatform Platform { get; } = new();
        /// <summary>待验证的服务。</summary>
        internal UuRemoteSessionRepairService Service { get; }

        /// <summary>使用现有日志和时钟依赖构造服务。</summary>
        internal RepairTestContext()
        {
            Service = new UuRemoteSessionRepairService(Config, Platform, Time,
                NullLogger<UuRemoteSessionRepairService>.Instance);
        }

        /// <summary>逐步推进虚拟时钟，并为后台继续执行留出调度时间。</summary>
        internal async Task<UuRemoteSessionRepairResult> RunAsync(Task<UuRemoteSessionRepairResult> task)
        {
            var deadline = Stopwatch.StartNew();
            while (!task.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                Time.Advance(TimeSpan.FromMilliseconds(500));
                await Task.Delay(1);
            }
            return await task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }
}
