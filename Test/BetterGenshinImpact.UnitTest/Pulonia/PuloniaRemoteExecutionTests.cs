using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 新一条龙跨实例派发：主实例编排与落盘、子实例执行节点。用进程内回环通道连接派发代理和节点宿主，
/// 覆盖节点与快照的序列化往返、本地与远程共用串行队列、取消、断线、旧版本状态拒绝和子实例只读模式
/// </summary>
public sealed class PuloniaRemoteExecutionTests : IDisposable
{
    /// <summary>
    /// 每个测试独占的状态与历史目录
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>
    /// 测试使用的云原神执行目标
    /// </summary>
    private static readonly PuloniaTaskExecutionTarget WebViewTarget = new()
    {
        Kind = PuloniaTaskExecutionTargetKind.WebView,
        InstanceName = "test",
        DisplayName = "云原神（test）"
    };

    /// <summary>
    /// 远程运行的叶子节点由子实例执行器执行，结果、执行实例和历史都记录在主实例
    /// </summary>
    [Fact]
    public async Task RemoteRun_ExecutesLeafOnTargetAndRecordsResult()
    {
        using var store = new PuloniaTaskStore(_directory);
        var localRegistry = new PuloniaCSharpTaskRegistry();
        localRegistry.Register("test.op", (_, _, _) => throw new InvalidOperationException("不应在主实例执行"));
        var remoteRegistry = new PuloniaCSharpTaskRegistry();
        remoteRegistry.Register("test.op", (context, _, _) => Task.FromResult(
            PuloniaTaskOutcome.Success("子实例完成", new JObject { ["address"] = context.TaskAddress, ["at"] = "2024-01-01T00:00:00Z" })));
        var loopback = new LoopbackTransport();
        await using var service = CreateService(store, localRegistry, loopback, remoteRegistry);
        var plan = await SavePlanAsync(store, "test.op");

        var id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id, ExecutionTarget = WebViewTarget });
        var run = await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status);
        var result = Assert.Single(run.NodeResults);
        Assert.Equal(PuloniaTaskNodeStatus.Succeeded, result.Status);
        Assert.Equal("子实例完成", result.Message);
        // 数据经 JSON 往返后保持原样，形如日期的字符串不被改写
        Assert.Equal("2024-01-01T00:00:00Z", result.Data!.Value<string>("at"));
        Assert.Equal(WebViewTarget.LaneKey, run.ExecutionTarget!.LaneKey);
        var archived = Assert.Single(await store.ListHistoryAsync());
        Assert.Equal("云原神（test）", archived.Request.ExecutionTarget!.DisplayName);
        Assert.Null((await store.LoadStateAsync()).State.ActiveRun);
    }

    /// <summary>
    /// 主实例本地运行与子实例运行共用一个串行队列：本地运行结束前，远程运行保持排队，结束后再派发
    /// </summary>
    [Fact]
    public async Task LocalAndRemoteRuns_ShareOneSerialQueue()
    {
        using var store = new PuloniaTaskStore(_directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var localRegistry = new PuloniaCSharpTaskRegistry();
        localRegistry.Register("test.wait", async (_, _, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return PuloniaTaskOutcome.Success("不应自然结束");
        });
        var remoteRegistry = new PuloniaCSharpTaskRegistry();
        remoteRegistry.Register("test.op", (_, _, _) => Task.FromResult(PuloniaTaskOutcome.Success("远程完成")));
        await using var service = CreateService(store, localRegistry, new LoopbackTransport(), remoteRegistry);
        var localPlan = await SavePlanAsync(store, "test.wait");
        var remotePlan = await SavePlanAsync(store, "test.op");

        var localId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = localPlan.Id });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var remoteId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = remotePlan.Id, ExecutionTarget = WebViewTarget });

        // 给队列足够时间：若存在并行通道，远程运行早已完成
        await Task.Delay(300);
        Assert.Equal(PuloniaTaskRunStatus.Queued, (await service.GetRunAsync(remoteId)).Status);
        Assert.Equal(PuloniaTaskRunStatus.Running, (await service.GetRunAsync(localId)).Status);

        await service.CancelAsync(localId).WaitAsync(TimeSpan.FromSeconds(15));
        var remoteRun = await service.WaitForCompletionAsync(remoteId).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, remoteRun.Status);
        Assert.Equal(PuloniaTaskRunStatus.Cancelled, (await service.GetRunAsync(localId)).Status);
    }

    /// <summary>
    /// 子实例上的运行就是唯一活动运行，状态中记录执行实例；停止后子实例执行器退出，运行记为已取消
    /// </summary>
    [Fact]
    public async Task RemoteRun_IsTheActiveRun_AndCancelStopsRemoteNode()
    {
        using var store = new PuloniaTaskStore(_directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        var remoteRegistry = new PuloniaCSharpTaskRegistry();
        remoteRegistry.Register("test.wait", async (_, _, ct) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                return PuloniaTaskOutcome.Success("不应自然结束");
            }
            finally { cleaned = true; }
        });
        var localRegistry = new PuloniaCSharpTaskRegistry();
        localRegistry.Register("test.wait", (_, _, _) => throw new InvalidOperationException("不应在主实例执行"));
        await using var service = CreateService(store, localRegistry, new LoopbackTransport(), remoteRegistry);
        var plan = await SavePlanAsync(store, "test.wait");

        var id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id, ExecutionTarget = WebViewTarget });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var state = (await store.LoadStateAsync()).State;
        Assert.Equal(id, state.ActiveRun!.RunId);
        Assert.Equal(WebViewTarget.LaneKey, state.ActiveRun.Request.ExecutionTarget!.LaneKey);

        await service.CancelAsync(id).WaitAsync(TimeSpan.FromSeconds(15));
        var run = await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(cleaned);
        Assert.Equal(PuloniaTaskRunStatus.Cancelled, run.Status);
    }

    /// <summary>
    /// 执行实例断开时：主实例的节点立即失败，子实例取消正在执行的节点
    /// </summary>
    [Fact]
    public async Task Disconnect_FailsRemoteNode_AndCancelsOnTarget()
    {
        using var store = new PuloniaTaskStore(_directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledOnTarget = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remoteRegistry = new PuloniaCSharpTaskRegistry();
        remoteRegistry.Register("test.wait", async (_, _, ct) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                return PuloniaTaskOutcome.Success("不应自然结束");
            }
            catch (OperationCanceledException)
            {
                cancelledOnTarget.TrySetResult();
                throw;
            }
        });
        var loopback = new LoopbackTransport();
        await using var service = CreateService(store, new PuloniaCSharpTaskRegistry(), loopback, remoteRegistry);
        var plan = await SavePlanAsync(store, "test.wait");

        var id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id, ExecutionTarget = WebViewTarget });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        loopback.Disconnect();

        var run = await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(15));
        await cancelledOnTarget.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(PuloniaTaskRunStatus.Failed, run.Status);
        var result = Assert.Single(run.NodeResults);
        Assert.Contains("断开", result.Message);
    }

    /// <summary>
    /// 执行实例不在线时拒绝提交，不产生排队请求
    /// </summary>
    [Fact]
    public async Task OfflineTarget_IsRejectedAtSubmit()
    {
        using var store = new PuloniaTaskStore(_directory);
        var loopback = new LoopbackTransport { Online = false };
        await using var service = CreateService(store, new PuloniaCSharpTaskRegistry(), loopback, new PuloniaCSharpTaskRegistry());
        var plan = await SavePlanAsync(store, "test.op");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id, ExecutionTarget = WebViewTarget }));
        Assert.Contains("不在线", error.Message);
        Assert.Empty(await service.ListRunsAsync());
    }

    /// <summary>
    /// 子实例中的服务不是协调器：拒绝提交，关闭时也不创建或改写共享的 state.json
    /// </summary>
    [Fact]
    public async Task NonCoordinator_RejectsSubmit_AndNeverWritesState()
    {
        var store = new PuloniaTaskStore(_directory);
        await using (var service = new PuloniaTaskService(store, new PuloniaTaskBuilder(store),
                         [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())], new PuloniaGameTaskCoordinator(null!),
                         new TaskStopService(NullLogger<TaskStopService>.Instance), isCoordinator: false))
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.EnqueueAsync(new PuloniaTaskRequest { PlanId = "test" }));
            Assert.Contains("子实例", error.Message);
            Assert.Empty(await service.ListRunsAsync());
        }

        store.Dispose();
        Assert.False(File.Exists(Path.Combine(_directory, "state.json")));
    }

    /// <summary>
    /// 主实例重启时，遗留的远程活动运行与本地活动运行一样转为中断历史，可以续跑
    /// </summary>
    [Fact]
    public async Task Restart_ArchivesLeftoverRemoteActiveRunAsInterrupted()
    {
        string id;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remoteRegistry = new PuloniaCSharpTaskRegistry();
        remoteRegistry.Register("test.wait", async (_, _, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return PuloniaTaskOutcome.Success("不应自然结束");
        });
        PuloniaTaskState leftover;
        using (var store = new PuloniaTaskStore(_directory))
        {
            var service = CreateService(store, new PuloniaCSharpTaskRegistry(), new LoopbackTransport(), remoteRegistry);
            var plan = await SavePlanAsync(store, "test.wait");
            id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id, ExecutionTarget = WebViewTarget });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // 模拟主实例在运行期间崩溃：只取当时落盘的状态，不走正常关闭流程
            leftover = (await store.LoadStateAsync()).State;
            await service.DisposeAsync();
        }

        Assert.Equal(id, leftover.ActiveRun!.RunId);
        Assert.NotNull(leftover.ActiveRun.Request.ExecutionTarget);
        var restored = Path.Combine(_directory, "restored");
        using var restartedStore = new PuloniaTaskStore(restored);
        await restartedStore.SaveStateAsync(leftover);
        await using var restarted = CreateService(restartedStore, new PuloniaCSharpTaskRegistry(), new LoopbackTransport(),
            new PuloniaCSharpTaskRegistry());

        var run = await restarted.GetRunAsync(id);
        Assert.Equal(PuloniaTaskRunStatus.Interrupted, run.Status);
        Assert.True(run.IsHistorical);
        Assert.Null((await restartedStore.LoadStateAsync()).State.ActiveRun);
    }

    /// <summary>
    /// 保存一个只有单个 C# 节点的计划
    /// </summary>
    private static Task<PuloniaTaskPlan> SavePlanAsync(PuloniaTaskStore store, string operation)
        => store.SavePlanAsync(new PuloniaTaskPlan
        {
            Name = "跨实例 " + operation,
            RootTask = new PuloniaTask
            {
                Name = "根",
                Children = [new PuloniaTask { Name = "节点", TaskType = "csharp", Parameters = new JObject { ["operation"] = operation } }]
            }
        });

    /// <summary>
    /// 建立主实例协调器，并用回环通道连接到“子实例”的节点宿主；两侧使用各自的执行器与停止服务
    /// </summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store, PuloniaCSharpTaskRegistry localRegistry,
        LoopbackTransport loopback, PuloniaCSharpTaskRegistry remoteRegistry)
    {
        var broker = new PuloniaRemoteExecutionBroker(() => loopback);
        var host = new PuloniaRemoteNodeHost(() => loopback, [new PuloniaCSharpTaskExecutor(remoteRegistry)],
            new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance),
            NullLogger<PuloniaRemoteNodeHost>.Instance);
        loopback.Connect(broker, host);
        return new PuloniaTaskService(store, new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(localRegistry)],
            new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance),
            remoteDispatcher: broker);
    }

    /// <summary>
    /// 只清理本测试的独占临时目录
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// 进程内回环通道：主实例侧调用直接转给节点宿主，子实例侧回报直接转给派发代理；
    /// 每条消息都按真实实例通信的帧格式写入再读出，覆盖序列化往返
    /// </summary>
    private sealed class LoopbackTransport : IPuloniaRemoteTransport
    {
        /// <summary>
        /// 模拟的子实例进程 ID
        /// </summary>
        private const int ChildProcessId = 4242;

        /// <summary>
        /// 主实例侧派发代理
        /// </summary>
        private PuloniaRemoteExecutionBroker? _broker;

        /// <summary>
        /// 子实例侧节点宿主
        /// </summary>
        private PuloniaRemoteNodeHost? _host;

        /// <summary>
        /// 子实例是否在线
        /// </summary>
        public bool Online { get; set; } = true;

        /// <inheritdoc />
        public event Action<int>? RemoteInstanceDisconnected;

        /// <inheritdoc />
        public event Action? RootConnectionLost;

        /// <summary>
        /// 连接两侧
        /// </summary>
        public void Connect(PuloniaRemoteExecutionBroker broker, PuloniaRemoteNodeHost host)
        {
            _broker = broker;
            _host = host;
        }

        /// <summary>
        /// 模拟连接断开：两侧都收到断开通知
        /// </summary>
        public void Disconnect()
        {
            Online = false;
            RemoteInstanceDisconnected?.Invoke(ChildProcessId);
            RootConnectionLost?.Invoke();
        }

        /// <inheritdoc />
        public int? ResolveTargetProcessId(PuloniaTaskExecutionTarget target) => Online ? ChildProcessId : null;

        /// <inheritdoc />
        public async Task StartRemoteNodeAsync(int processId, PuloniaRemoteNodeStart start, CancellationToken ct)
        {
            EnsureOnline();
            _host!.Start(await RoundtripAsync(start));
        }

        /// <inheritdoc />
        public async Task CancelRemoteNodeAsync(int processId, Guid executionId, CancellationToken ct)
        {
            EnsureOnline();
            var cancel = await RoundtripAsync(new PuloniaRemoteNodeCancel { ExecutionId = executionId });
            _host!.Cancel(cancel.ExecutionId);
        }

        /// <inheritdoc />
        public async Task FinishRemoteNodeAsync(PuloniaRemoteNodeFinished finished, CancellationToken ct)
        {
            EnsureOnline();
            _broker!.ReceiveFinished(ChildProcessId, await RoundtripAsync(finished));
        }

        /// <summary>
        /// 断开后与真实通道一样拒绝发送
        /// </summary>
        private void EnsureOnline()
        {
            if (!Online) throw new InvalidOperationException("当前尚未连接 BetterGI 根实例。");
        }

        /// <summary>
        /// 按实例通信的帧格式写入并读出一条消息
        /// </summary>
        private static async Task<T> RoundtripAsync<T>(T value) where T : class
        {
            using var stream = new MemoryStream();
            await BetterGenshinImpact.Service.Instance.InstanceIpcProtocol.WriteJsonAsync(stream,
                BetterGenshinImpact.Service.Instance.InstanceIpcEnvelope.Request("test", value), CancellationToken.None);
            stream.Position = 0;
            var frame = await BetterGenshinImpact.Service.Instance.InstanceIpcProtocol.ReadFrameAsync(stream, CancellationToken.None);
            return BetterGenshinImpact.Service.Instance.InstanceIpcProtocol.ReadJson(frame!.Value).Data!
                .ToObject<T>(BetterGenshinImpact.Service.Instance.InstanceIpcProtocol.Serializer)!;
        }
    }
}
