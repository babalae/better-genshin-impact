using System.Runtime.CompilerServices;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 执行记录的跨实例运行视图（统筹设计 6.5）：合并本实例运行、其他实例 <c>instances/*/state.json</c> 与共享历史，
/// 同一运行以历史为准；本实例的 state.json 不从磁盘读取；修改标记的变化与按标记缓存；读不出的 state.json 进入失败列表。
/// 另覆盖执行记录页的停止权限（本实例、主实例下发、连接断开、其他实例自己的运行），
/// 以及本实例续跑其他实例在本实例启动后归档的运行。其他实例用指向同一目录的独立存储对象模拟，不启动主程序、不触碰游戏。
/// </summary>
public sealed class PuloniaRunOverviewTests : IDisposable
{
    /// <summary>本测试独占的目录，不接触程序真实 User/Pulonia。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>样本运行所属的计划 ID；历史写在 history/&lt;计划 ID&gt;/ 下。</summary>
    private const string PlanId = "overview-test";

    /// <summary>主实例（本实例）的实例键。</summary>
    private const string Primary = PuloniaInstanceKey.Primary;

    /// <summary>云原神实例“a”的实例键。</summary>
    private static readonly string KeyA = PuloniaInstanceKey.ForWebView("a");

    /// <summary>云原神实例“b”的实例键。</summary>
    private static readonly string KeyB = PuloniaInstanceKey.ForWebView("b");

    /// <summary>模拟的在线云原神子实例“b”，主实例下发运行到它。</summary>
    private static readonly InstanceEndpoint ChildB = new()
    {
        InstanceType = BetterGiInstanceType.WebView, ProcessId = 3002, WindowsSessionId = 1, InstanceName = "b"
    };

    /// <summary>
    /// 合并：其他实例 state.json 中的排队、活动与待归档运行出现在结果中；同一运行同时在其他实例 state 与历史中时以历史为准、不重复；
    /// 本实例的运行取自任务服务，磁盘上本实例的 state.json 不读取。
    /// </summary>
    [Fact]
    public async Task List_MergesOtherInstanceStatesWithHistory()
    {
        using var store = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        // 1. 实例 a：一个排队、一个运行中；实例 b：一个待归档，且已写入共享历史（历史中的消息不同）
        var queued = Record(KeyA, PuloniaTaskRunStatus.Queued, "a 排队中");
        var active = Record(KeyA, PuloniaTaskRunStatus.Running, "a 运行中");
        await otherStore.SaveStateAsync(KeyA, new PuloniaTaskState { PendingRequests = [queued], ActiveRun = active });
        var pendingArchive = Record(KeyB, PuloniaTaskRunStatus.Succeeded, "b 待归档");
        await otherStore.SaveStateAsync(KeyB, new PuloniaTaskState { PendingArchives = [pendingArchive] });
        await otherStore.ArchiveRunAsync(Record(KeyB, PuloniaTaskRunStatus.Succeeded, "b 已归档", pendingArchive.RunId));
        // 2. 磁盘上本实例的 state.json 有一个排队请求；本实例的运行只来自任务服务
        var ownOnDisk = Record(Primary, PuloniaTaskRunStatus.Queued, "磁盘上的本实例排队");
        await otherStore.SaveStateAsync(Primary, new PuloniaTaskState { PendingRequests = [ownOnDisk] });
        var service = new FakeTaskService();
        var local = Record(Primary, PuloniaTaskRunStatus.Running, "本实例运行中");
        service.Runs.Add(new PuloniaTaskRunView(local, isHistorical: false));
        var overview = new PuloniaRunOverview(service, store, Primary);

        var result = await overview.ListAsync();

        Assert.Empty(result.StateFailures);
        Assert.Equal(new[] { queued.RunId, active.RunId, pendingArchive.RunId, local.RunId }.Order(),
            result.Runs.Select(run => run.RunId).Order());
        Assert.True(result.Runs.Single(run => run.RunId == queued.RunId) is
            { Status: PuloniaTaskRunStatus.Queued, IsHistorical: false, Message: "a 排队中" });
        Assert.Equal(KeyA, result.Runs.Single(run => run.RunId == active.RunId).Instance);
        var archived = result.Runs.Single(run => run.RunId == pendingArchive.RunId);
        Assert.True(archived.IsHistorical);
        Assert.Equal("b 已归档", archived.Message);
        Assert.Equal("本实例运行中", result.Runs.Single(run => run.RunId == local.RunId).Message);
        Assert.DoesNotContain(result.Runs, run => run.RunId == ownOnDisk.RunId);
        Assert.Equal(overview.GetChangeStamp(), result.ChangeStamp);
        // 3. 其他实例正在执行的运行（标题栏用）：只有 a 的活动运行
        var running = Assert.Single(await overview.ListOtherActiveRunsAsync());
        Assert.Equal((KeyA, active.RunId), (running.Key, running.Value.RunId));
    }

    /// <summary>
    /// 修改标记：其他实例写入或改写 state.json、新增历史文件时变化；没有变化或只改本实例的 state.json 时相同。
    /// 标记不变时复用缓存的历史（原地改写历史文件并保持目录时间与文件数，读取结果不变），标记变化后重新读取。
    /// </summary>
    [Fact]
    public async Task ChangeStamp_TracksOtherStatesAndHistory_AndHistoryIsCachedByStamp()
    {
        using var store = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        var overview = new PuloniaRunOverview(new FakeTaskService(), store, Primary);
        var empty = overview.GetChangeStamp();
        Assert.Equal(empty, (await overview.ListAsync()).ChangeStamp);
        Assert.Equal(empty, overview.GetChangeStamp());

        // 1. 其他实例写入 state.json：变化；未再写入时相同
        await otherStore.SaveStateAsync(KeyA, new PuloniaTaskState { PendingRequests = [Record(KeyA, PuloniaTaskRunStatus.Queued, "排队一")] });
        var withState = overview.GetChangeStamp();
        Assert.NotEqual(empty, withState);
        Assert.Equal(withState, overview.GetChangeStamp());

        // 2. 改写 state.json：变化
        await otherStore.SaveStateAsync(KeyA, new PuloniaTaskState
        {
            Sequence = 1,
            PendingRequests = [Record(KeyA, PuloniaTaskRunStatus.Queued, "排队一"), Record(KeyA, PuloniaTaskRunStatus.Queued, "排队二")]
        });
        var rewritten = overview.GetChangeStamp();
        Assert.NotEqual(withState, rewritten);

        // 3. 本实例自己的 state.json 不计入标记
        await otherStore.SaveStateAsync(Primary, new PuloniaTaskState { PendingRequests = [Record(Primary, PuloniaTaskRunStatus.Queued, "本实例")] });
        Assert.Equal(rewritten, overview.GetChangeStamp());

        // 4. 新增历史文件：变化；读取结果记下同一标记
        var history = Record(KeyA, PuloniaTaskRunStatus.Failed, "原消息");
        await otherStore.ArchiveRunAsync(history);
        var withHistory = overview.GetChangeStamp();
        Assert.NotEqual(rewritten, withHistory);
        var listed = await overview.ListAsync();
        Assert.Equal(withHistory, listed.ChangeStamp);
        Assert.Equal("原消息", listed.Runs.Single(run => run.RunId == history.RunId).Message);
        // 同一计划再归档一份：文件数变化
        await otherStore.ArchiveRunAsync(Record(KeyA, PuloniaTaskRunStatus.Succeeded, "第二份"));
        var withSecond = overview.GetChangeStamp();
        Assert.NotEqual(withHistory, withSecond);
        Assert.Equal(2, (await overview.ListAsync()).Runs.Count(run => run.IsHistorical));

        // 5. 原地改写历史文件，并恢复目录时间（文件数不变）：标记不变，复用缓存，不重读历史
        var historyRoot = Path.Combine(_directory, "history");
        var planDirectory = Path.Combine(historyRoot, PlanId);
        var rootTime = Directory.GetLastWriteTimeUtc(historyRoot);
        var planTime = Directory.GetLastWriteTimeUtc(planDirectory);
        await File.WriteAllTextAsync(Path.Combine(planDirectory, history.RunId + ".json"),
            PuloniaTaskJson.WriteRunRecord(Record(KeyA, PuloniaTaskRunStatus.Failed, "改写后", history.RunId)));
        Directory.SetLastWriteTimeUtc(planDirectory, planTime);
        Directory.SetLastWriteTimeUtc(historyRoot, rootTime);
        Assert.Equal(withSecond, overview.GetChangeStamp());
        Assert.Equal("原消息", (await overview.ListAsync()).Runs.Single(run => run.RunId == history.RunId).Message);

        // 6. 目录时间变化后标记变化，重新读取历史
        Directory.SetLastWriteTimeUtc(planDirectory, planTime.AddMinutes(1));
        Assert.NotEqual(withSecond, overview.GetChangeStamp());
        Assert.Equal("改写后", (await overview.ListAsync()).Runs.Single(run => run.RunId == history.RunId).Message);
    }

    /// <summary>
    /// 读不出的其他实例 state.json 进入失败列表而不抛出，其余实例照常显示；执行记录页据此提示，文件修好后提示清除。
    /// </summary>
    [Fact]
    public async Task UnreadableState_IsReportedWithoutThrowing()
    {
        using var store = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        var queued = Record(KeyA, PuloniaTaskRunStatus.Queued, "a 排队中");
        await otherStore.SaveStateAsync(KeyA, new PuloniaTaskState { PendingRequests = [queued] });
        var badPath = Path.Combine(_directory, "instances", "webview-b", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(badPath)!);
        await File.WriteAllTextAsync(badPath, "{ 损坏");
        var service = new FakeTaskService();
        var overview = new PuloniaRunOverview(service, store, Primary);

        // 1. 坏文件进入失败列表，其他实例的运行照常显示；标题栏的活动运行查询同样不抛出
        var result = await overview.ListAsync();
        Assert.Equal(queued.RunId, Assert.Single(result.Runs).RunId);
        var failure = Assert.Single(result.StateFailures);
        Assert.Equal(Path.GetFullPath(badPath), Path.GetFullPath(failure.Path));
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
        Assert.Empty(await overview.ListOtherActiveRunsAsync());
        Assert.Equal("{ 损坏", await File.ReadAllTextAsync(badPath));

        // 2. 执行记录页显示说明，文件修好后清除
        var vm = new PuloniaTaskHistoryViewModel(service, store, overview);
        await vm.RefreshAsync();
        Assert.Contains("云原神 · b", vm.StatusMessage);
        Assert.Contains("无法读取", vm.StatusMessage);
        Assert.Equal(queued.RunId, Assert.Single(vm.Runs).RunId);
        await otherStore.SaveStateAsync(KeyB, new PuloniaTaskState());
        await vm.RefreshAsync();
        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.Empty((await overview.ListAsync()).StateFailures);
    }

    /// <summary>
    /// 停止权限：本实例的活动运行可停止；其他实例自己的活动运行不可停止并提示到该实例界面停止；
    /// 主实例下发且连接正常的运行可停止，连接断开后不可停止；已结束的运行不可停止。
    /// </summary>
    [Fact]
    public async Task StopAccess_OnlyLocalAndConnectedDispatchedRunsCanStop()
    {
        using var store = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        // 1. 本实例运行中；a 自己的运行；主实例下发到 b 的运行；a 已结束并归档的运行
        var service = new FakeTaskService();
        var local = Record(Primary, PuloniaTaskRunStatus.Running, "本实例运行中");
        service.Runs.Add(new PuloniaTaskRunView(local, isHistorical: false));
        var other = Record(KeyA, PuloniaTaskRunStatus.Running, "a 自己的运行");
        await otherStore.SaveStateAsync(KeyA, new PuloniaTaskState { ActiveRun = other });
        var dispatched = Record(KeyB, PuloniaTaskRunStatus.Running, "主实例下发到 b");
        await otherStore.SaveStateAsync(KeyB, new PuloniaTaskState { ActiveRun = dispatched });
        var finished = Record(KeyA, PuloniaTaskRunStatus.Failed, "a 已结束");
        await otherStore.ArchiveRunAsync(finished);
        var transport = new DisconnectableRunTransport();
        var dispatcher = new PuloniaRunDispatcher(() => transport);
        dispatcher.ReceiveChanged(ChildB, PuloniaRunChangedReport.FromView(new PuloniaTaskRunView(dispatched, isHistorical: false)));
        var vm = new PuloniaTaskHistoryViewModel(service, store, new PuloniaRunOverview(service, store, Primary), dispatcher);

        await vm.RefreshAsync();

        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.Equal(4, vm.Runs.Count);
        Assert.True(vm.ShowInstanceBadges);
        // 2. 本实例的活动运行：可停止
        var localItem = vm.Runs.Single(item => item.RunId == local.RunId);
        Assert.Equal((PuloniaRunStopAccess.Local, true), (localItem.StopAccess, localItem.CanCancel));
        Assert.Equal("停止这次运行，并等待当前任务退出", localItem.StopToolTip);
        // 3. 其他实例自己的活动运行：显示停止按钮但不可点击，提示到该实例界面停止
        var otherItem = vm.Runs.Single(item => item.RunId == other.RunId);
        Assert.True(otherItem.IsActive);
        Assert.Equal((PuloniaRunStopAccess.OtherInstance, false), (otherItem.StopAccess, otherItem.CanCancel));
        Assert.Equal("这次运行在“云原神 · a”执行，请在该实例界面停止", otherItem.StopToolTip);
        // 4. 主实例下发、连接正常：可停止
        var dispatchedItem = vm.Runs.Single(item => item.RunId == dispatched.RunId);
        Assert.Equal((PuloniaRunStopAccess.Dispatched, true), (dispatchedItem.StopAccess, dispatchedItem.CanCancel));
        Assert.Equal("运行中", dispatchedItem.StatusText);
        // 5. 已结束的运行：不可停止
        var finishedItem = vm.Runs.Single(item => item.RunId == finished.RunId);
        Assert.True(finishedItem.IsHistorical);
        Assert.False(finishedItem.IsActive);
        Assert.False(finishedItem.CanCancel);
        // 6. 选中项决定停止命令能否执行
        vm.SelectedRun = otherItem;
        Assert.False(vm.CanCancelRun);
        Assert.False(vm.CancelRunCommand.CanExecute(null));
        vm.SelectedRun = localItem;
        Assert.True(vm.CanCancelRun);
        Assert.True(vm.CancelRunCommand.CanExecute(null));

        // 7. 与 b 的连接断开：下发的运行不可停止并说明原因
        transport.Disconnect(ChildB);
        await vm.RefreshAsync();
        dispatchedItem = vm.Runs.Single(item => item.RunId == dispatched.RunId);
        Assert.Equal((PuloniaRunStopAccess.Disconnected, false), (dispatchedItem.StopAccess, dispatchedItem.CanCancel));
        Assert.Contains("连接已断开", dispatchedItem.StopToolTip);
        // 断开期间最后收到的状态可能已过时，显示“连接断开，状态未知”并按需关注着色
        Assert.Equal(("连接断开，状态未知", "attention"), (dispatchedItem.StatusText, dispatchedItem.StatusKind));
        Assert.True(vm.Runs.Single(item => item.RunId == local.RunId).CanCancel);
    }

    /// <summary>
    /// 续跑：其他实例在本实例服务启动后才归档的运行不在本实例的常驻历史索引中，仍可通过运行 ID 在共享历史中找到并在本实例续跑；
    /// 已成功的节点跳过，续跑的执行实例记为本实例。
    /// </summary>
    [Fact]
    public async Task Resume_RunArchivedByOtherInstanceAfterStartup()
    {
        // 每个实例在自己的进程中有自己的存储对象，这里各建一个指向同一目录
        using var primaryStore = new PuloniaTaskStore(_directory);
        using var childStore = new PuloniaTaskStore(_directory);
        var plan = await primaryStore.SavePlanAsync(new PuloniaTaskPlan
        {
            Name = "跨实例续跑",
            RootTask = new PuloniaTask { Name = "根", Children = [Operation("第一步", "test.first"), Operation("第二步", "test.second")] }
        });
        // 本实例：记录两个节点各执行了几次；子实例：第一步成功、第二步失败
        var primaryFirst = 0;
        var primarySecond = 0;
        var primaryRegistry = new PuloniaCSharpTaskRegistry();
        primaryRegistry.Register("test.first", (_, _, _) =>
        {
            Interlocked.Increment(ref primaryFirst);
            return Task.FromResult(PuloniaTaskOutcome.Success("本实例第一步"));
        });
        primaryRegistry.Register("test.second", (_, _, _) =>
        {
            Interlocked.Increment(ref primarySecond);
            return Task.FromResult(PuloniaTaskOutcome.Success("本实例第二步"));
        });
        var childRegistry = new PuloniaCSharpTaskRegistry();
        childRegistry.Register("test.first", (_, _, _) => Task.FromResult(PuloniaTaskOutcome.Success("子实例第一步")));
        childRegistry.Register("test.second", (_, _, _) => Task.FromResult(PuloniaTaskOutcome.Failure("子实例第二步失败")));

        // 1. 本实例服务先启动并加载常驻历史索引（此时没有历史）
        await using var primary = CreateService(primaryStore, primaryRegistry, Primary);
        Assert.Empty(await primary.ListRunsAsync());

        // 2. 子实例在本实例启动后运行并归档到共享历史
        PuloniaTaskRunView failed;
        await using (var child = CreateService(childStore, childRegistry, KeyA))
            failed = await child.WaitForCompletionAsync(await child.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id }))
                .WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal((PuloniaTaskRunStatus.Failed, KeyA), (failed.Status, failed.Instance));
        Assert.True(primaryStore.HistoryExists(plan.Id, failed.RunId));
        Assert.DoesNotContain(await primary.ListRunsAsync(), run => run.RunId == failed.RunId);

        // 3. 本实例续跑：只执行未成功的第二步，执行实例为本实例
        var resumed = await primary.WaitForCompletionAsync(await primary.ResumeAsync(failed.RunId)).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, resumed.Status);
        Assert.Equal((Primary, failed.RunId), (resumed.Instance, resumed.ResumedFromRunId));
        Assert.Equal((0, 1), (Volatile.Read(ref primaryFirst), Volatile.Read(ref primarySecond)));
    }

    /// <summary>只清理本测试的独占临时目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// 建立序列化必要字段齐全的运行记录；未结束的状态没有结束时间，排队的没有开始时间。
    /// </summary>
    /// <param name="instance">执行实例键</param>
    /// <param name="status">运行状态</param>
    /// <param name="message">状态消息，用于区分同一运行的不同版本</param>
    /// <param name="runId">运行 ID；为空时新建</param>
    private static PuloniaTaskRunRecord Record(string instance, PuloniaTaskRunStatus status, string message, string? runId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new PuloniaTaskRunRecord
        {
            RunId = runId ?? PuloniaId.NewRunId(DateTimeOffset.Now),
            Instance = instance,
            Request = new PuloniaTaskRequest { PlanId = PlanId },
            PlanName = "跨实例样本",
            SnapshotJson = "{}",
            Status = status,
            Message = message,
            SubmittedAt = now,
            StartedAt = status == PuloniaTaskRunStatus.Queued ? null : now,
            FinishedAt = PuloniaTaskRunClassifier.IsActive(status) ? null : now
        };
    }

    /// <summary>建立一个 C# 操作节点。</summary>
    /// <param name="name">节点名称</param>
    /// <param name="operation">C# 操作名</param>
    private static PuloniaTask Operation(string name, string operation)
        => new() { Name = name, TaskType = "csharp", Parameters = new JObject { ["operation"] = operation } };

    /// <summary>建立指定实例键的纯 C# 服务，模拟该实例进程中的协调器。</summary>
    /// <param name="store">该实例的存储对象</param>
    /// <param name="registry">C# 操作注册表</param>
    /// <param name="instanceKey">实例键</param>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store, PuloniaCSharpTaskRegistry registry, string instanceKey)
        => new(store, new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(registry)], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance),
            accountService: new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(instanceKey)));

    /// <summary>
    /// 本实例任务服务的替身：只提供给定的本实例运行，供跨实例运行视图与执行记录页读取；其余操作在本测试中不应调用。
    /// </summary>
    private sealed class FakeTaskService : IPuloniaTaskService
    {
        /// <summary>本实例内存中的运行，由测试直接填入。</summary>
        public List<PuloniaTaskRunView> Runs { get; } = [];

        /// <inheritdoc />
        public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [];

        /// <inheritdoc />
        public string? RecoveryNotice => null;

        /// <inheritdoc />
        public event EventHandler<PuloniaTaskRunChangedEventArgs>? RunChanged { add { } remove { } }

        /// <inheritdoc />
        public Task<IReadOnlyList<PuloniaTaskRunView>> ListRunsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PuloniaTaskRunView>>(Runs.ToArray());

        /// <inheritdoc />
        public Task<string> EnqueueAsync(PuloniaTaskRequest request, CancellationToken ct = default) => throw Unexpected();

        /// <inheritdoc />
        public Task<string> EnqueueDispatchedAsync(string planId, long planRevision, string? targetTaskId, CancellationToken ct = default)
            => throw Unexpected();

        /// <inheritdoc />
        public Task<PuloniaTaskRunView> GetRunAsync(string runId, CancellationToken ct = default) => throw Unexpected();

        /// <inheritdoc />
        public Task<PuloniaTaskRunView> WaitForCompletionAsync(string runId, CancellationToken ct = default) => throw Unexpected();

        /// <inheritdoc />
        public Task CancelAsync(string runId, CancellationToken ct = default) => throw Unexpected();

        /// <inheritdoc />
        public Task RequestCancelAsync(string runId, CancellationToken ct = default) => throw Unexpected();

        /// <inheritdoc />
        public Task<string> ResumeAsync(string runId, string? startTaskAddress = null, bool allowUncertainReplay = false,
            CancellationToken ct = default) => throw Unexpected();

        /// <inheritdoc />
        public Task<PuloniaTaskRunPreview> PreviewAsync(PuloniaTaskRequest request, string? accountId = null, CancellationToken ct = default)
            => throw Unexpected();

        /// <inheritdoc />
        public Task CheckTriggersAsync(IReadOnlyList<PuloniaTaskPlan> plans, IReadOnlyCollection<string>? unreadablePlanIds = null,
            CancellationToken ct = default) => throw Unexpected();

        /// <inheritdoc />
        public Task FireHotkeyAsync(string planId, string triggerId, CancellationToken ct = default) => throw Unexpected();

        /// <inheritdoc />
        public IReadOnlyList<PuloniaTaskTriggerStatus> GetTriggerStatuses() => throw Unexpected();

        /// <summary>本测试不应调用的操作。</summary>
        /// <param name="member">被调用的成员名</param>
        private static NotSupportedException Unexpected([CallerMemberName] string member = "")
            => new($"跨实例运行视图测试不应调用 {member}。");
    }

    /// <summary>
    /// 可模拟子实例断开的替身通道：本测试只用它的断开通知，不下发、不停止、不上报。
    /// </summary>
    private sealed class DisconnectableRunTransport : IPuloniaRunTransport
    {
        /// <inheritdoc />
        public event Action<InstanceEndpoint>? ChildInstanceDisconnected;

        /// <inheritdoc />
        public event Action? RootConnected { add { } remove { } }

        /// <summary>模拟子实例断开连接。</summary>
        /// <param name="endpoint">断开的子实例端点</param>
        public void Disconnect(InstanceEndpoint endpoint) => ChildInstanceDisconnected?.Invoke(endpoint);

        /// <inheritdoc />
        public Task<PuloniaRunSubmitResponse> SubmitRunAsync(InstanceEndpoint target, PuloniaRunSubmitRequest request, CancellationToken ct)
            => throw new InvalidOperationException("本测试不下发运行。");

        /// <inheritdoc />
        public Task CancelRunAsync(InstanceEndpoint target, PuloniaRunCancelRequest request, CancellationToken ct)
            => throw new InvalidOperationException("本测试不停止运行。");

        /// <inheritdoc />
        public Task ReportRunChangedAsync(PuloniaRunChangedReport report, CancellationToken ct)
            => throw new InvalidOperationException("主实例不上报运行状态。");
    }
}
