using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Instance.MessageHandlers;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 一步下发（pulonia.run.*）：主实例的下发客户端经进程内回环通道与子实例的下发宿主相连，消息按真实实例通信的帧格式往返，
/// 两侧都经过实例通信的消息处理器（含方向校验）。子实例是完整的本地协调器，运行状态与历史写在共享目录中它自己的实例目录。
/// 覆盖：修订号一致入队并回运行 ID、不一致拒绝；只能停止下发的运行且不等待运行结束；只回报下发的运行；
/// 断开不取消运行、重连补发；方向校验。
/// </summary>
public sealed class PuloniaRunDispatchTests : IDisposable
{
    /// <summary>每个测试独占的 Pulonia 目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>子实例（云原神实例“Test”）的实例键。</summary>
    private static readonly string ChildKey = PuloniaInstanceKey.ForWebView("Test");

    /// <summary>修订号一致：子实例固定快照、放入自己的队列并回运行 ID；运行记录标明执行实例与下发来源，主实例显示上报的最终状态。</summary>
    [Fact]
    public async Task Submit_WithCurrentRevision_EnqueuesOnChildAndReturnsRunId()
    {
        await using var harness = await Harness.CreateAsync(_directory);
        harness.Registry.Register("test.op", (_, _, _) => Task.FromResult(PuloniaTaskOutcome.Success("子实例完成")));
        var plan = await harness.SavePlanAsync("test.op");

        var runId = await harness.Dispatcher.SubmitAsync(harness.Loopback.ChildEndpoint, plan.Id, plan.Revision);

        var run = await harness.Child.WaitForCompletionAsync(runId).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status);
        Assert.Equal(ChildKey, run.Instance);
        Assert.True(run.IsDispatched);
        Assert.Equal("子实例完成", Assert.Single(run.NodeResults).Message);
        // 主实例只显示：上报的最终状态到达后不再是排队中
        await WaitUntilAsync(() => harness.Dispatcher.ListRuns().SingleOrDefault()?.Status == PuloniaTaskRunStatus.Succeeded);
        var shown = Assert.Single(harness.Dispatcher.ListRuns());
        Assert.Equal((runId, plan.Id, ChildKey, "云原神 · Test"), (shown.RunId, shown.PlanId, shown.Instance, shown.InstanceName));
        // 状态与历史写在子实例自己的实例目录与共享历史中，主实例目录没有运行状态
        Assert.True(File.Exists(Path.Combine(_directory, "instances", "webview-test", "state.json")));
        Assert.False(File.Exists(harness.Store.GetStatePath(PuloniaInstanceKey.Primary)));
        var archived = Assert.Single(await harness.Store.ListHistoryAsync());
        Assert.Equal((ChildKey, PuloniaTaskRequest.DispatchSource), (archived.Instance, archived.Request.Source));
        var historyJson = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "history", plan.Id, runId + ".json")));
        Assert.Equal(ChildKey, historyJson.Value<string>("instance"));
        Assert.False(((JObject)historyJson["request"]!).ContainsKey("execution_target"));
    }

    /// <summary>修订号不一致：子实例拒绝并回原因，不产生排队请求，主实例也不跟踪。</summary>
    [Fact]
    public async Task Submit_WithStaleRevision_IsRejected()
    {
        await using var harness = await Harness.CreateAsync(_directory);
        var plan = await harness.SavePlanAsync("test.op");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Dispatcher.SubmitAsync(harness.Loopback.ChildEndpoint, plan.Id, plan.Revision - 1));

        Assert.Contains("计划已变更，请重新运行", error.Message);
        Assert.Empty(await harness.Child.ListRunsAsync());
        Assert.Empty(harness.Dispatcher.ListRuns());
    }

    /// <summary>计划无效等准备失败：原因随失败响应回到主实例。</summary>
    [Fact]
    public async Task Submit_MissingPlan_ReturnsReasonToPrimary()
    {
        await using var harness = await Harness.CreateAsync(_directory);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Dispatcher.SubmitAsync(harness.Loopback.ChildEndpoint, PuloniaId.NewPlanId(), 1));

        Assert.Contains("不存在", error.Message);
        Assert.Empty(await harness.Child.ListRunsAsync());
    }

    /// <summary>
    /// 停止：子实例自己提交的运行不能被主实例停止；下发的运行只发出取消即响应，执行器仍在退出时请求已经返回，
    /// 之后最终状态经上报到达主实例。
    /// </summary>
    [Fact]
    public async Task Cancel_OnlyStopsDispatchedRuns_AndDoesNotWaitForExit()
    {
        await using var harness = await Harness.CreateAsync(_directory);
        var localEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Registry.Register("test.local", async (_, _, ct) =>
        {
            localEntered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return PuloniaTaskOutcome.Success("不应自然结束");
        });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Registry.Register("test.stubborn", async (_, _, _) =>
        {
            // 不观察取消令牌：模拟收尾较慢的执行器
            entered.TrySetResult();
            await release.Task;
            return PuloniaTaskOutcome.Success("收尾完成");
        });
        var localPlan = await harness.SavePlanAsync("test.local");
        var dispatchedPlan = await harness.SavePlanAsync("test.stubborn");

        // 1. 子实例自己提交的运行：主实例的停止请求被拒绝，运行不受影响
        var localId = await harness.Child.EnqueueAsync(new PuloniaTaskRequest { PlanId = localPlan.Id, Source = "ui" });
        await localEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ((IPuloniaRunTransport)harness.Loopback).CancelRunAsync(harness.Loopback.ChildEndpoint,
                new PuloniaRunCancelRequest { RunId = localId }, CancellationToken.None));
        Assert.Contains("只能停止主实例下发的运行", rejected.Message);
        Assert.Equal(PuloniaTaskRunStatus.Running, (await harness.Child.GetRunAsync(localId)).Status);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => harness.Dispatcher.CancelAsync(localId));
        await harness.Child.CancelAsync(localId).WaitAsync(TimeSpan.FromSeconds(15));

        // 2. 下发的运行：停止请求在执行器退出前就返回
        var runId = await harness.Dispatcher.SubmitAsync(harness.Loopback.ChildEndpoint, dispatchedPlan.Id, dispatchedPlan.Revision);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await harness.Dispatcher.CancelAsync(runId).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(release.Task.IsCompleted);
        Assert.Equal(PuloniaTaskRunStatus.Cancelling, (await harness.Child.GetRunAsync(runId)).Status);
        // 主实例先显示“停止中”；已在途的旧上报可能短暂覆盖，随后的上报会再次带来停止中
        await WaitUntilAsync(() => harness.Dispatcher.ListRuns().Single(run => run.RunId == runId).Status == PuloniaTaskRunStatus.Cancelling);

        // 3. 执行器退出后运行记为已取消，最终状态上报到主实例
        release.TrySetResult();
        Assert.Equal(PuloniaTaskRunStatus.Cancelled,
            (await harness.Child.WaitForCompletionAsync(runId).WaitAsync(TimeSpan.FromSeconds(15))).Status);
        await WaitUntilAsync(() => harness.Dispatcher.ListRuns().Single(run => run.RunId == runId).Status == PuloniaTaskRunStatus.Cancelled);
    }

    /// <summary>状态上报只针对主实例下发的运行；子实例自己提交的运行不经过实例通信。</summary>
    [Fact]
    public async Task Changed_ReportsOnlyDispatchedRuns()
    {
        await using var harness = await Harness.CreateAsync(_directory);
        harness.Registry.Register("test.op", (_, _, _) => Task.FromResult(PuloniaTaskOutcome.Success("完成")));
        var plan = await harness.SavePlanAsync("test.op");

        var localId = await harness.Child.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id, Source = "ui" });
        await harness.Child.WaitForCompletionAsync(localId).WaitAsync(TimeSpan.FromSeconds(15));
        var runId = await harness.Dispatcher.SubmitAsync(harness.Loopback.ChildEndpoint, plan.Id, plan.Revision);
        await harness.Child.WaitForCompletionAsync(runId).WaitAsync(TimeSpan.FromSeconds(15));
        await WaitUntilAsync(() => harness.Dispatcher.ListRuns().SingleOrDefault()?.Status == PuloniaTaskRunStatus.Succeeded);

        var reported = harness.Loopback.GetReportedRunIds();
        Assert.NotEmpty(reported);
        Assert.All(reported, item => Assert.Equal(runId, item));
        Assert.Equal(runId, Assert.Single(harness.Dispatcher.ListRuns()).RunId);
    }

    /// <summary>
    /// 断开：子实例上的运行照常执行，主实例显示“连接断开”且不能远程停止；断开期间结束的运行，
    /// 重新连接后补发最终状态，主实例恢复显示。
    /// </summary>
    [Fact]
    public async Task Disconnect_DoesNotCancelRun_AndReconnectResendsState()
    {
        await using var harness = await Harness.CreateAsync(_directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = false;
        harness.Registry.Register("test.wait", async (_, _, ct) =>
        {
            entered.TrySetResult();
            try { await release.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { cancelled = true; throw; }
            return PuloniaTaskOutcome.Success("断开期间完成");
        });
        var plan = await harness.SavePlanAsync("test.wait");
        var runId = await harness.Dispatcher.SubmitAsync(harness.Loopback.ChildEndpoint, plan.Id, plan.Revision);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await WaitUntilAsync(() => harness.Dispatcher.ListRuns().Single().Status == PuloniaTaskRunStatus.Running);

        // 1. 断开：主实例标记状态未知，不能远程停止；子实例的运行不受影响
        harness.Loopback.Disconnect();
        var shown = Assert.Single(harness.Dispatcher.ListRuns());
        Assert.True(shown.IsDisconnected);
        Assert.False(shown.CanCancel);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Dispatcher.CancelAsync(runId));
        Assert.Contains("连接已断开", error.Message);
        await Task.Delay(200);
        Assert.False(cancelled);
        Assert.Equal(PuloniaTaskRunStatus.Running, (await harness.Child.GetRunAsync(runId)).Status);

        // 2. 断开期间运行结束：最终状态无法送达，主实例仍显示断开
        release.TrySetResult();
        Assert.Equal(PuloniaTaskRunStatus.Succeeded,
            (await harness.Child.WaitForCompletionAsync(runId).WaitAsync(TimeSpan.FromSeconds(15))).Status);
        Assert.True(Assert.Single(harness.Dispatcher.ListRuns()).IsDisconnected);

        // 3. 重新连接：补发没有送达的最终状态
        harness.Loopback.Reconnect();
        await WaitUntilAsync(() => harness.Dispatcher.ListRuns().Single() is { IsDisconnected: false, Status: PuloniaTaskRunStatus.Succeeded });
        Assert.False(cancelled);
    }

    /// <summary>
    /// 主实例重启：新的下发客户端不知道之前下发的运行；子实例重新连接后补发尚未结束的下发运行，新客户端据此显示并可以停止它。
    /// </summary>
    [Fact]
    public async Task Reconnect_ResendsUnfinishedRuns_ToRestartedPrimary()
    {
        await using var harness = await Harness.CreateAsync(_directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Registry.Register("test.wait", async (_, _, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return PuloniaTaskOutcome.Success("不应自然结束");
        });
        var plan = await harness.SavePlanAsync("test.wait");
        var runId = await harness.Dispatcher.SubmitAsync(harness.Loopback.ChildEndpoint, plan.Id, plan.Revision);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));

        // 1. 主实例崩溃并重启：连接断开，新的下发客户端没有任何记录
        harness.Loopback.Disconnect();
        var restarted = new PuloniaRunDispatcher(() => harness.Loopback);
        harness.Loopback.ConnectPrimary(restarted);
        Assert.Empty(restarted.ListRuns());

        // 2. 子实例重新连上：补发尚未结束的运行
        harness.Loopback.Reconnect();
        await WaitUntilAsync(() => restarted.ListRuns().SingleOrDefault()?.Status == PuloniaTaskRunStatus.Running);
        var learned = Assert.Single(restarted.ListRuns());
        Assert.Equal((runId, plan.Id, ChildKey, false), (learned.RunId, learned.PlanId, learned.Instance, learned.IsDisconnected));

        // 3. 新客户端可以停止它
        await restarted.CancelAsync(runId).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PuloniaTaskRunStatus.Cancelled,
            (await harness.Child.WaitForCompletionAsync(runId).WaitAsync(TimeSpan.FromSeconds(15))).Status);
        await WaitUntilAsync(() => restarted.ListRuns().Single().Status == PuloniaTaskRunStatus.Cancelled);
    }

    /// <summary>
    /// 方向校验：submit、cancel 只接受来自主实例的请求，主实例不接受；changed 只接受已登记子实例发给主实例的上报。
    /// </summary>
    [Fact]
    public async Task Handlers_EnforceDirection()
    {
        await using var harness = await Harness.CreateAsync(_directory);
        var submit = InstanceIpcEnvelope.Request(InstanceOperations.PuloniaRunSubmit,
            new PuloniaRunSubmitRequest { PlanId = PuloniaId.NewPlanId(), PlanRevision = 1 });
        var cancel = InstanceIpcEnvelope.Request(InstanceOperations.PuloniaRunCancel, new PuloniaRunCancelRequest { RunId = "run_x" });
        var changed = InstanceIpcEnvelope.Request(InstanceOperations.PuloniaRunChanged,
            new PuloniaRunChangedReport { RunId = "run_x", PlanId = PuloniaId.NewPlanId() });
        var child = harness.Loopback.ChildHandler;
        var primary = harness.Loopback.PrimaryHandler;
        var root = harness.Loopback.RootEndpoint;
        var sibling = harness.Loopback.ChildEndpoint;

        // 1. 子实例只接受来自主实例的下发与停止：来自其他子实例或尚未登记的连接都拒绝
        await Assert.ThrowsAsync<InvalidOperationException>(() => child.HandleSubmitAsync(sibling, submit, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => child.HandleSubmitAsync(null, submit, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => child.HandleCancelAsync(sibling, cancel, CancellationToken.None));
        // 2. 主实例不接受下发与停止
        await Assert.ThrowsAsync<InvalidOperationException>(() => primary.HandleSubmitAsync(root, submit, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => primary.HandleCancelAsync(root, cancel, CancellationToken.None));
        // 3. 状态上报只能由已登记的子实例发给主实例
        Assert.Throws<InvalidOperationException>(() => child.HandleChanged(root, changed));
        Assert.Throws<InvalidOperationException>(() => primary.HandleChanged(root, changed));
        Assert.Throws<InvalidOperationException>(() => primary.HandleChanged(null, changed));
        Assert.Empty(harness.Dispatcher.ListRuns());
        Assert.Empty(await harness.Child.ListRunsAsync());
        // 4. 合法方向可以通过
        Assert.True(primary.HandleChanged(sibling, changed).Success);
        Assert.Equal("run_x", Assert.Single(harness.Dispatcher.ListRuns()).RunId);
    }

    /// <summary>轮询等待条件成立，最多 15 秒；上报在后台发送，测试不能假定同步完成。</summary>
    /// <param name="condition">要等待的条件</param>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("等待条件成立超时。");
            await Task.Delay(20);
        }
    }

    /// <summary>只清理本测试的独占临时目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// 测试装置：共享目录中的子实例协调器与下发宿主、主实例的下发客户端，以及连接两侧的回环通道。
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        /// <summary>共享的 Pulonia 存储（同一 User/Pulonia 目录）。</summary>
        public required PuloniaTaskStore Store { get; init; }

        /// <summary>子实例执行器使用的 C# 操作注册表。</summary>
        public required PuloniaCSharpTaskRegistry Registry { get; init; }

        /// <summary>子实例的本地协调器。</summary>
        public required PuloniaTaskService Child { get; init; }

        /// <summary>子实例的下发宿主。</summary>
        public required PuloniaRunDispatchHost Host { get; init; }

        /// <summary>主实例的下发客户端。</summary>
        public required PuloniaRunDispatcher Dispatcher { get; init; }

        /// <summary>连接两侧的回环通道。</summary>
        public required LoopbackRunTransport Loopback { get; init; }

        /// <summary>
        /// 建立装置并让子实例宿主开始回报。
        /// </summary>
        /// <param name="directory">测试目录</param>
        public static Task<Harness> CreateAsync(string directory)
        {
            var store = new PuloniaTaskStore(directory);
            var registry = new PuloniaCSharpTaskRegistry();
            var keyProvider = new PuloniaInstanceKeyProvider(ChildKey);
            var child = new PuloniaTaskService(store, new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(registry)],
                new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance),
                accountService: new PuloniaAccountService(store, keyProvider));
            var loopback = new LoopbackRunTransport();
            var host = new PuloniaRunDispatchHost(child, keyProvider, () => loopback);
            var dispatcher = new PuloniaRunDispatcher(() => loopback);
            loopback.ConnectChild(host);
            loopback.ConnectPrimary(dispatcher);
            host.Start();
            return Task.FromResult(new Harness
            {
                Store = store, Registry = registry, Child = child, Host = host, Dispatcher = dispatcher, Loopback = loopback
            });
        }

        /// <summary>保存一个只有单个 C# 节点的计划。</summary>
        /// <param name="operation">C# 操作名</param>
        public Task<PuloniaTaskPlan> SavePlanAsync(string operation)
            => Store.SavePlanAsync(new PuloniaTaskPlan
            {
                Name = "下发 " + operation,
                RootTask = new PuloniaTask
                {
                    Name = "根",
                    Children = [new PuloniaTask { Name = "节点", TaskType = "csharp", Parameters = new JObject { ["operation"] = operation } }]
                }
            });

        /// <summary>停止回报并释放子实例协调器与存储。</summary>
        public async ValueTask DisposeAsync()
        {
            Host.Stop();
            await Child.DisposeAsync();
            Store.Dispose();
        }
    }

    /// <summary>
    /// 进程内回环通道：主实例侧调用转给子实例的消息处理器，子实例侧上报转给主实例的消息处理器；
    /// 请求与响应都按真实实例通信的帧格式写入再读出，处理器抛出的可预期错误按实例通信的规则转为失败响应。
    /// </summary>
    private sealed class LoopbackRunTransport : IPuloniaRunTransport
    {
        /// <summary>模拟的主实例端点。</summary>
        public InstanceEndpoint RootEndpoint { get; } = new()
        {
            InstanceType = BetterGiInstanceType.Primary, ProcessId = 1000, WindowsSessionId = 1
        };

        /// <summary>模拟的云原神子实例端点。</summary>
        public InstanceEndpoint ChildEndpoint { get; } = new()
        {
            InstanceType = BetterGiInstanceType.WebView, ProcessId = 4242, WindowsSessionId = 1, InstanceName = "Test"
        };

        /// <summary>子实例侧的消息处理器。</summary>
        public PuloniaRunMessageHandler ChildHandler { get; private set; } = null!;

        /// <summary>主实例侧的消息处理器。</summary>
        public PuloniaRunMessageHandler PrimaryHandler { get; private set; } = null!;

        /// <summary>主实例收到的状态上报的运行 ID（按到达顺序），在 <see cref="_deliveryGate"/> 内写入。</summary>
        private readonly List<string> _reportedRunIds = [];

        /// <summary>主实例已收到的状态上报的运行 ID 快照。</summary>
        public string[] GetReportedRunIds()
        {
            lock (_deliveryGate)
                return [.. _reportedRunIds];
        }

        /// <summary>连接是否正常。</summary>
        private volatile bool _online = true;

        /// <summary>串行化状态上报的处理与断开、重连，模拟真实连接中消息与断开通知的先后顺序。</summary>
        private readonly object _deliveryGate = new();

        /// <inheritdoc />
        public event Action<InstanceEndpoint>? ChildInstanceDisconnected;

        /// <inheritdoc />
        public event Action? RootConnected;

        /// <summary>接入子实例侧：子实例上下文的处理器只使用下发宿主。</summary>
        /// <param name="host">子实例的下发宿主</param>
        public void ConnectChild(PuloniaRunDispatchHost host)
            => ChildHandler = new PuloniaRunMessageHandler(new InstanceContext(BetterGiInstanceType.WebView, "test-pipe", null, "Test"),
                () => host, () => throw new InvalidOperationException("子实例不使用下发客户端。"));

        /// <summary>接入（或替换为重启后的）主实例侧：主实例上下文的处理器只使用下发客户端。</summary>
        /// <param name="dispatcher">主实例的下发客户端</param>
        public void ConnectPrimary(PuloniaRunDispatcher dispatcher)
            => PrimaryHandler = new PuloniaRunMessageHandler(new InstanceContext(BetterGiInstanceType.Primary, "test-pipe", null),
                () => throw new InvalidOperationException("主实例不使用下发宿主。"), () => dispatcher);

        /// <summary>模拟连接断开：主实例收到子实例断开通知，之后两个方向的发送都失败。</summary>
        public void Disconnect()
        {
            lock (_deliveryGate)
            {
                _online = false;
                ChildInstanceDisconnected?.Invoke(ChildEndpoint);
            }
        }

        /// <summary>模拟子实例重新连上主实例并完成登记。</summary>
        public void Reconnect()
        {
            lock (_deliveryGate)
                _online = true;
            RootConnected?.Invoke();
        }

        /// <inheritdoc />
        public async Task<PuloniaRunSubmitResponse> SubmitRunAsync(InstanceEndpoint target, PuloniaRunSubmitRequest request,
            CancellationToken ct)
        {
            var envelope = await RoundtripAsync(InstanceIpcEnvelope.Request(InstanceOperations.PuloniaRunSubmit, request));
            var response = await InvokeAsync(envelope, () => ChildHandler.HandleSubmitAsync(RootEndpoint, envelope, ct));
            return response.Data!.ToObject<PuloniaRunSubmitResponse>(InstanceIpcProtocol.Serializer)!;
        }

        /// <inheritdoc />
        public async Task CancelRunAsync(InstanceEndpoint target, PuloniaRunCancelRequest request, CancellationToken ct)
        {
            var envelope = await RoundtripAsync(InstanceIpcEnvelope.Request(InstanceOperations.PuloniaRunCancel, request));
            await InvokeAsync(envelope, () => ChildHandler.HandleCancelAsync(RootEndpoint, envelope, ct));
        }

        /// <inheritdoc />
        public async Task ReportRunChangedAsync(PuloniaRunChangedReport report, CancellationToken ct)
        {
            var envelope = await RoundtripAsync(InstanceIpcEnvelope.Request(InstanceOperations.PuloniaRunChanged, report));
            InstanceIpcEnvelope response;
            // 与真实连接一样：上报要么在断开之前处理完，要么在断开之后被拒绝，不会在断开通知之后才到达
            lock (_deliveryGate)
            {
                if (!_online)
                    throw new InvalidOperationException("当前尚未连接 BetterGI 根实例。");
                try
                {
                    response = PrimaryHandler.HandleChanged(ChildEndpoint, envelope);
                    _reportedRunIds.Add(report.RunId);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    response = InstanceIpcEnvelope.Failure(envelope, "invalid_request", exception.GetBaseException().Message);
                }
            }
            response = await RoundtripAsync(response);
            if (response.Success != true)
                throw new InvalidOperationException(response.ErrorMessage ?? "实例 IPC 请求失败。");
        }

        /// <summary>
        /// 调用对端处理器：断开时与真实通道一样拒绝发送；可预期的错误按实例通信规则转为失败响应，再由发送方转为异常。
        /// </summary>
        /// <param name="request">请求信封</param>
        /// <param name="handle">对端处理</param>
        /// <returns>成功的响应信封（已经过帧格式往返）</returns>
        private async Task<InstanceIpcEnvelope> InvokeAsync(InstanceIpcEnvelope request, Func<Task<InstanceIpcEnvelope>> handle)
        {
            if (!_online)
                throw new InvalidOperationException("当前尚未连接 BetterGI 根实例。");
            InstanceIpcEnvelope response;
            try
            {
                response = await handle();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException
                                                  or TimeoutException or Newtonsoft.Json.JsonException)
            {
                response = InstanceIpcEnvelope.Failure(request, "invalid_request", exception.GetBaseException().Message);
            }
            response = await RoundtripAsync(response);
            if (response.Success != true)
                throw new InvalidOperationException(response.ErrorMessage ?? "实例 IPC 请求失败。");
            return response;
        }

        /// <summary>按实例通信的帧格式写入并读出一个信封。</summary>
        /// <param name="envelope">信封</param>
        private static async Task<InstanceIpcEnvelope> RoundtripAsync(InstanceIpcEnvelope envelope)
        {
            using var stream = new MemoryStream();
            await InstanceIpcProtocol.WriteJsonAsync(stream, envelope, CancellationToken.None);
            stream.Position = 0;
            var frame = await InstanceIpcProtocol.ReadFrameAsync(stream, CancellationToken.None);
            return InstanceIpcProtocol.ReadJson(frame!.Value);
        }
    }
}
