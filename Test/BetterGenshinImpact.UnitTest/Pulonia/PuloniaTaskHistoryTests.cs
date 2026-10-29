using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ClearScript.V8;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 历史持久化、类型专属展示与选择回归，不启动 WPF 应用、游戏或截图器。
/// </summary>
public sealed class PuloniaTaskHistoryTests : IDisposable
{
    /// <summary>
    /// 每个测试独占的临时目录，不使用程序真实 User/Pulonia。
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    // 确认事实的归档与重启回归随通用额度框架移除，改由 PuloniaCollectionCooldownTests 以文件级 CD 覆盖。

    /// <summary>
    /// 重新打开存储后，类型专属返回值、多行中文日志与结构化证据仍完整保留。
    /// </summary>
    [Fact]
    public async Task LocalArchive_KeepsResultDataAndEvidence()
    {
        var record = CreateRecord();
        record.NodeResults.Add(new PuloniaTaskNodeResult("history-test/shell", "Shell 日志", "shell", 1,
            PuloniaTaskNodeStatus.Succeeded, "进程结束", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            new JObject { ["exit_code"] = 0, ["standard_output"] = "中文日志\n下一行", ["standard_output_truncated"] = true },
            PuloniaTaskOutcomeKind.ExecutedUnverified,
            [new PuloniaTaskEvidence { Source = "history-test", Data = new JObject { ["proof"] = 42 } }]));
        using (var store = new PuloniaTaskStore(_directory))
            await store.ArchiveRunAsync(record);
        using var reopened = new PuloniaTaskStore(_directory);
        var restored = Assert.Single(await reopened.ListHistoryAsync());
        var node = Assert.Single(restored.NodeResults);
        Assert.Equal("中文日志\n下一行", node.Data.Value<string>("standard_output"));
        Assert.True(node.Data.Value<bool>("standard_output_truncated"));
        Assert.Equal(PuloniaTaskOutcomeKind.ExecutedUnverified, node.OutcomeKind);
        Assert.Equal(42, Assert.Single(node.Evidence).Data.Value<int>("proof"));
    }

    /// <summary>
    /// 损坏确认事件不得绕过运行历史身份校验。
    /// </summary>
    [Fact]
    public void History_RejectsInvalidConfirmedEffect()
    {
        var record = CreateRecord();
        record.ConfirmedEffects.Add(new PuloniaTaskConfirmedEffect { Attempt = 0, TaskAddress = "wrong" });
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskJson.ReadRunRecord(PuloniaTaskJson.WriteRunRecord(record)));
    }

    /// <summary>
    /// 停止运行环境应中断游戏等待，清理完成前保持停止中，之后刷新和重启均显示已停止（取消）的本地历史。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RuntimeStop_FinishesAndArchivesAfterExecutorCleanup(bool usesCancellationToken)
    {
        using var store = new PuloniaTaskStore(_directory);
        var stopService = new TaskStopService(NullLogger<TaskStopService>.Instance);
        var registry = new PuloniaCSharpTaskRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtimeAvailable = 1;
        var cleaned = false;
        registry.Register("test.runtime_wait", async (_, _, ct) =>
        {
            try
            {
                // 复用生产等待边界，替换窗口存活查询；不加载 TaskControl 的应用日志宿主。
                var wait = new GameTaskWait(() =>
                {
                    entered.TrySetResult();
                    return Volatile.Read(ref runtimeAvailable) == 1;
                }, usesCancellationToken ? ct : CancellationToken.None);
                await Task.Run(() => wait.Sleep(60000));
                return PuloniaTaskOutcome.Success("不应完成整段等待");
            }
            finally
            {
                cleanupEntered.TrySetResult();
                await allowCleanup.Task;
                cleaned = true;
            }
        });
        registry.Register("test.after_cleanup", (_, _, _) =>
        {
            Assert.True(cleaned);
            return Task.FromResult(PuloniaTaskOutcome.Success("旧执行器已释放资源"));
        });
        var plan = await store.SavePlanAsync(CreatePlan("停止游戏运行环境", "test.runtime_wait"));
        var nextPlan = await store.SavePlanAsync(CreatePlan("清理后运行", "test.after_cleanup"));
        string requestId;
        await using (var service = CreateService(store, registry, stopService))
        {
            try
            {
                requestId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                var model = new PuloniaTaskHistoryViewModel(service, store);
                await model.RefreshAsync(requestId);
                Assert.Equal("运行中", model.SelectedRun!.StatusText);

                // 与运行环境关闭的顺序一致：先广播停止，再使原环境不可用。
                stopService.StopAll(TaskStopReason.RuntimeStopped);
                Volatile.Write(ref runtimeAvailable, 0);
                await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                var stopping = await service.GetRunAsync(requestId);
                Assert.Equal(PuloniaTaskRunStatus.Cancelling, stopping.Status);
                Assert.Contains("运行环境已停止", stopping.Message);
                Assert.False(stopping.IsHistorical);
                Assert.False(service.WaitForCompletionAsync(requestId).IsCompleted);
                await model.RefreshAsync(requestId);
                Assert.Equal("停止中", model.SelectedRun!.StatusText);

                var nextId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = nextPlan.Id });
                Assert.Equal(PuloniaTaskRunStatus.Queued, (await service.GetRunAsync(nextId)).Status);
                allowCleanup.TrySetResult();
                var finished = await service.WaitForCompletionAsync(requestId).WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal(PuloniaTaskRunStatus.Cancelled, finished.Status);
                Assert.True(finished.IsHistorical);
                Assert.NotNull(finished.FinishedAt);
                Assert.Null(finished.CurrentTaskAddress);
                Assert.Equal(PuloniaTaskNodeStatus.Cancelled, Assert.Single(finished.NodeResults).Status);
                Assert.Equal(PuloniaTaskRunStatus.Succeeded,
                    (await service.WaitForCompletionAsync(nextId).WaitAsync(TimeSpan.FromSeconds(15))).Status);
                var savedState = (await store.LoadStateAsync()).State;
                Assert.Null(savedState.ActiveRun);
                Assert.Empty(savedState.PendingRequests);
                Assert.Empty(savedState.PendingArchives);

                await model.RefreshAsync();
                Assert.Equal(requestId, model.SelectedRun!.RunId);
                Assert.Equal("已停止", model.SelectedRun.StatusText);
                Assert.True(model.SelectedRun.IsHistorical);
                Assert.False(model.SelectedRun.CanCancel);
                Assert.Equal(PuloniaTaskRunStatus.Cancelled,
                    (await store.ListHistoryAsync()).Single(item => item.RunId == requestId).Status);
            }
            finally
            {
                // 断言失败也释放受控清理门，避免测试服务关闭被自身的替身阻塞。
                Volatile.Write(ref runtimeAvailable, 0);
                allowCleanup.TrySetResult();
            }
        }

        await using var restarted = CreateService(store);
        var restored = await restarted.GetRunAsync(requestId);
        Assert.Equal(PuloniaTaskRunStatus.Cancelled, restored.Status);
        Assert.True(restored.IsHistorical);
        Assert.Equal("已停止", new PuloniaTaskRunItemViewModel(restored).StatusText);
    }

    /// <summary>
    /// 快捷键停止 JS 内等待的路线后，真实 V8 中断也必须完成取消归档，不能遗留停止中或提前启动下一节点。
    /// </summary>
    [Fact]
    public async Task HotkeyStop_JavaScriptRouteFinishesAndPersistsStoppedHistory()
    {
        using var store = new PuloniaTaskStore(_directory);
        var stopService = new TaskStopService(NullLogger<TaskStopService>.Instance);
        var registry = new PuloniaCSharpTaskRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        registry.Register("test.js_route", async (_, _, ct) =>
        {
            // 使用真实脚本生命周期，仅把游戏路线换成可控宿主；不加载应用、截图或输入后端。
            await ScriptExecution.ExecuteAsync(token =>
            {
                var engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableTaskPromiseConversion);
                engine.AddHostObject("route", new Func<Task>(async () =>
                {
                    using var operation = ScriptHostOperations.Enter();
                    entered.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        cleanupEntered.TrySetResult();
                        await allowCleanup.Task.ConfigureAwait(false);
                        cleaned = true;
                    }
                }));
                return engine;
            }, engine => engine.Evaluate("(async () => { try { await route(); } catch (error) {} })()"), ct);
            return PuloniaTaskOutcome.Success("不应在停止后返回成功");
        });
        registry.Register("test.js_after", (_, _, _) =>
        {
            Assert.True(cleaned);
            return Task.FromResult(PuloniaTaskOutcome.Success("JS 路线已收尾"));
        });
        var plan = await store.SavePlanAsync(CreatePlan("快捷键停止 JS 路线", "test.js_route"));
        var nextPlan = await store.SavePlanAsync(CreatePlan("JS 清理后运行", "test.js_after"));
        string requestId;
        await using (var service = CreateService(store, registry, stopService))
        {
            try
            {
                requestId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                // 热键入口同样广播 UserRequested，不创建另一套停止或运行状态。
                stopService.StopAll(TaskStopReason.UserRequested);
                await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal(PuloniaTaskRunStatus.Cancelling, (await service.GetRunAsync(requestId)).Status);
                var nextId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = nextPlan.Id });
                Assert.Equal(PuloniaTaskRunStatus.Queued, (await service.GetRunAsync(nextId)).Status);
                allowCleanup.TrySetResult();

                var finished = await service.WaitForCompletionAsync(requestId).WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal(PuloniaTaskRunStatus.Cancelled, finished.Status);
                Assert.True(finished.IsHistorical);
                Assert.NotNull(finished.FinishedAt);
                Assert.Equal(PuloniaTaskNodeStatus.Cancelled, Assert.Single(finished.NodeResults).Status);
                Assert.Equal(PuloniaTaskRunStatus.Succeeded,
                    (await service.WaitForCompletionAsync(nextId).WaitAsync(TimeSpan.FromSeconds(15))).Status);
                var model = new PuloniaTaskHistoryViewModel(service, store);
                await model.RefreshAsync(requestId);
                Assert.Equal("已停止", model.SelectedRun!.StatusText);
                Assert.False(model.SelectedRun.CanCancel);
                Assert.Equal(PuloniaTaskRunStatus.Cancelled,
                    (await store.ListHistoryAsync()).Single(item => item.RunId == requestId).Status);
            }
            finally
            {
                allowCleanup.TrySetResult();
            }
        }
        await using var restarted = CreateService(store);
        var restored = await restarted.GetRunAsync(requestId);
        Assert.True(restored.IsHistorical);
        Assert.Equal("已停止", new PuloniaTaskRunItemViewModel(restored).StatusText);
    }

    /// <summary>
    /// 小于两小时的耗时不能因四舍五入显示为两小时。
    /// </summary>
    [Fact]
    public void Duration_DoesNotRoundHoursUp()
    {
        var start = DateTimeOffset.UtcNow;
        Assert.Equal("1 小时 40 分", PuloniaTaskHistoryText.Duration(start, start.AddMinutes(100)));
    }

    /// <summary>
    /// 建立纯 C# 服务，游戏协调器的运行时永不使用，不启动游戏会话。
    /// </summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store,
        PuloniaCSharpTaskRegistry? registry = null, TaskStopService? stopService = null) => new(store,
        new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(registry ?? new PuloniaCSharpTaskRegistry())],
        new PuloniaGameTaskCoordinator(null!), stopService ?? new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>
    /// 建立受控计算计划。
    /// </summary>
    private static PuloniaTaskPlan CreatePlan(string name, string operation) => new()
    {
        Name = name,
        RootTask = new PuloniaTask
        {
            Name = "根分组", Children =
            [new PuloniaTask
            {
                Name = "测试节点", TaskType = "csharp",
                Parameters = new JObject { ["operation"] = operation, ["values"] = new JArray(1, 2, 3) }
            }]
        }
    };

    /// <summary>
    /// 建立序列化必要字段齐全的历史样本。
    /// </summary>
    private static PuloniaTaskRunRecord CreateRecord() => new()
    {
        RunId = PuloniaId.NewRunId(DateTimeOffset.Now), Request = new PuloniaTaskRequest { PlanId = "history-test" },
        PlanName = "历史样本", SnapshotJson = "{}", Status = PuloniaTaskRunStatus.Succeeded,
        Message = "测试结束", SubmittedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// 仅清理本测试生成的唯一临时目录，不接触真实程序数据。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
