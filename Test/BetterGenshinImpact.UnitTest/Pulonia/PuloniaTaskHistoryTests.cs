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
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pulonia-history-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 正常成功与部分成功都应归档确认事实，重复报告只保存一次，重建服务后仍可查看。
    /// </summary>
    [Theory]
    [InlineData("sample.ledger.daily", PuloniaTaskRunStatus.Succeeded)]
    [InlineData("sample.ledger.partial", PuloniaTaskRunStatus.Failed)]
    public async Task ConfirmedEffects_SurviveRestartWithoutDuplicates(string operation, PuloniaTaskRunStatus expectedStatus)
    {
        using var store = new PuloniaTaskStore(_directory);
        var plan = await store.SavePlanAsync(CreatePlan("确认事件", operation));
        Guid requestId;
        await using (var service = CreateService(store))
        {
            requestId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
            var run = await service.WaitForCompletionAsync(requestId).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(expectedStatus, run.Status);
            Assert.True(run.IsHistorical);
            Assert.Single(run.ConfirmedEffects);
            Assert.Single(await service.ListLedgerAsync());
            var effect = run.ConfirmedEffects[0];
            Assert.Equal(1, effect.Attempt);
            Assert.Equal(run.NodeResults[0].TaskAddress, effect.TaskAddress);

            // 查询返回对象的修改不能污染运行内部已落盘的确认事实。
            effect.Entry.Evidence.Data["sample"] = false;
            Assert.True((await service.GetRunAsync(requestId)).ConfirmedEffects[0].Entry.Evidence.Data.Value<bool>("sample"));
        }
        await using var restarted = CreateService(store);
        var restored = await restarted.GetRunAsync(requestId);
        Assert.Single(restored.ConfirmedEffects);
        Assert.True(restored.ConfirmedEffects[0].Entry.Evidence.Data.Value<bool>("sample"));
        var history = Assert.Single(await store.ListHistoryAsync());
        Assert.Single(history.ConfirmedEffects);
        var path = Path.Combine(_directory, "history", plan.Id, history.RunId.ToString("N") + ".json");
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        var node = new PuloniaTaskRunItemViewModel(restored).NodeDetails[0];
        Assert.Contains(node.EvidenceFields, field => field.Label.StartsWith("已确认副作用"));
    }

    /// <summary>
    /// 旧格式缺少新确认事件集合时应兼容，不能补造确认事实。
    /// </summary>
    [Fact]
    public void OldHistory_WithoutConfirmedEffectsStillLoads()
    {
        var json = JObject.Parse(PuloniaTaskJson.WriteRunRecord(CreateRecord()));
        json.Remove("confirmed_effects");
        Assert.Empty(PuloniaTaskJson.ReadRunRecord(json.ToString()).ConfirmedEffects);
    }

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
    /// Shell 两路输出、退出码与截断标记必须按真实值展示，未知扩展字段仍可查看。
    /// </summary>
    [Fact]
    public void ShellDetails_KeepOutputAndTruncationFlags()
    {
        var node = CreateNode("shell", new JObject
        {
            ["exit_code"] = 5, ["standard_output"] = "中文输出\n第二行", ["standard_error"] = "error",
            ["standard_output_truncated"] = true, ["standard_error_truncated"] = false, ["extra"] = "extended"
        });
        Assert.Equal("5", node.ResultFields.Single(field => field.Label == "进程退出码").Value);
        Assert.Equal("中文输出\n第二行", node.ResultFields.Single(field => field.Label == "标准输出").Value);
        Assert.Equal("是", node.ResultFields.Single(field => field.Label == "标准输出已截断").Value);
        Assert.Equal("否", node.ResultFields.Single(field => field.Label == "标准错误已截断").Value);
        Assert.Equal("extended", node.ResultFields.Single(field => field.Label == "extra").Value);
    }

    /// <summary>
    /// 尚未返回的数据不能冒充退出码 0、路线失败或空输出。
    /// </summary>
    [Theory]
    [InlineData("shell", "进程退出码")]
    [InlineData("pathing", "路线完整执行")]
    public void MissingResult_IsNotInvented(string taskType, string label)
    {
        var node = CreateNode(taskType, new JObject());
        Assert.StartsWith("未返回", node.ResultFields.Single(field => field.Label == label).Value);
    }

    /// <summary>
    /// 各类型的结果解释具有各自核验边界，不使用统一成功文案掩盖证据缺失。
    /// </summary>
    [Theory]
    [InlineData("pathing", "逐点采集")]
    [InlineData("javascript", "JS")]
    [InlineData("keymouse", "宏回放")]
    [InlineData("csharp", "C#")]
    [InlineData("builtin.claim_mail", "领取/合成")]
    public void TaskTypes_HaveSpecificVerificationNotes(string taskType, string expected)
    {
        var node = CreateNode(taskType, new JObject { ["extension"] = new JObject { ["detail"] = 42 } });
        Assert.Contains(expected, node.VerificationNote);
        Assert.Contains("42", node.ResultFields.Single(field => field.Label == "extension").Value);
    }

    /// <summary>
    /// 新运行刷新不抢占旧记录与节点选择，输入详情不受后续计划编辑影响。
    /// </summary>
    [Fact]
    public async Task HistoryRefresh_PreservesSelectionAndSnapshotInputs()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var oldPlan = await store.SavePlanAsync(CreatePlan("旧计划", "sample.sum"));
        var oldRequestId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = oldPlan.Id });
        await service.WaitForCompletionAsync(oldRequestId).WaitAsync(TimeSpan.FromSeconds(20));
        var model = new PuloniaTaskHistoryViewModel(service, store);
        await model.RefreshAsync(oldRequestId);
        var selected = model.SelectedRun;
        var selectedNode = model.SelectedRunNodeResult;
        oldPlan.RootTask.Children[0].Parameters["values"] = new JArray(99);
        await store.SavePlanAsync(oldPlan);
        var newRequestId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = oldPlan.Id });
        await service.WaitForCompletionAsync(newRequestId).WaitAsync(TimeSpan.FromSeconds(20));
        await model.RefreshAsync();
        Assert.Same(selected, model.SelectedRun);
        Assert.Same(selectedNode, model.SelectedRunNodeResult);
        var input = selectedNode!.InputFields.Single(field => field.Label.StartsWith("计算输入")).Value;
        Assert.DoesNotContain("99", input);
        Assert.Contains("1", input);
    }

    /// <summary>
    /// 有历史记录的计划首次打开时允许不选记录，显式定位仍能查看指定运行。
    /// </summary>
    [Fact]
    public async Task HistoryRefresh_WithExistingRunsDoesNotForceSelection()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var plan = await store.SavePlanAsync(CreatePlan("已有记录的计划", "sample.sum"));
        var requestId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        await service.WaitForCompletionAsync(requestId).WaitAsync(TimeSpan.FromSeconds(20));
        var model = new PuloniaTaskHistoryViewModel(service, store) { PlanFilterId = plan.Id };

        await model.RefreshAsync();
        Assert.Single(model.Runs);
        Assert.Null(model.SelectedRun);
        Assert.Null(model.SelectedRunNodeResult);
        Assert.False(model.HasSelectedRun);

        await model.RevealRunAsync(requestId);
        Assert.Equal(requestId, model.SelectedRun!.RequestId);
        Assert.NotNull(model.SelectedRunNodeResult);
    }

    /// <summary>
    /// 未选中记录是有效状态：刷新、新记录与切换计划均不能强制重新选择，也不影响历史数据。
    /// </summary>
    [Fact]
    public async Task PlanSwitchAndRefresh_DoNotForceSelection()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var plan = await store.SavePlanAsync(CreatePlan("计划甲", "sample.sum"));
        var otherPlan = await store.SavePlanAsync(CreatePlan("计划乙", "sample.sum"));
        var oldId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        await service.WaitForCompletionAsync(oldId).WaitAsync(TimeSpan.FromSeconds(20));
        var model = new PuloniaTaskHistoryViewModel(service, store) { PlanFilterId = plan.Id };
        await model.RevealRunAsync(oldId);

        // 列表取消选中即回到未选中状态，不停止任务、不删除记录。
        model.SelectedRun = null;
        model.SelectedRunNodeResult = null;
        Assert.Null(model.SelectedRun);
        Assert.False(model.CancelRunCommand.CanExecute(null));
        Assert.False(model.ResumeRunCommand.CanExecute(null));
        Assert.False(model.ResumeFromNodeCommand.CanExecute(null));

        var newId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        var otherId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = otherPlan.Id });
        await service.WaitForCompletionAsync(newId).WaitAsync(TimeSpan.FromSeconds(20));
        await service.WaitForCompletionAsync(otherId).WaitAsync(TimeSpan.FromSeconds(20));
        await model.RefreshAsync();
        Assert.Equal(2, model.Runs.Count);
        Assert.Null(model.SelectedRun);
        model.PlanFilterId = otherPlan.Id;
        Assert.Single(model.Runs);
        Assert.Null(model.SelectedRun);
        model.PlanFilterId = plan.Id;
        Assert.Null(model.SelectedRun);
        Assert.Null(model.SelectedRunNodeResult);

        // 明确查看某次运行时仍恢复选择，历史及节点详情不受未选中状态影响。
        await model.RevealRunAsync(oldId);
        Assert.Equal(oldId, model.SelectedRun!.RequestId);
        Assert.NotNull(model.SelectedRunNodeResult);
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await service.GetRunAsync(oldId)).Status);
        Assert.Equal(3, (await store.ListHistoryAsync()).Count);
    }

    /// <summary>
    /// 指定请求不存在时，刷新不能改为选择无关记录。
    /// </summary>
    [Fact]
    public async Task Refresh_WithUnknownRequestDoesNotSelectUnrelatedRun()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var plan = await store.SavePlanAsync(CreatePlan("计划甲", "sample.sum"));
        var otherPlan = await store.SavePlanAsync(CreatePlan("计划乙", "sample.sum"));
        var firstId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        var secondId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = otherPlan.Id });
        await service.WaitForCompletionAsync(firstId).WaitAsync(TimeSpan.FromSeconds(20));
        await service.WaitForCompletionAsync(secondId).WaitAsync(TimeSpan.FromSeconds(20));
        var model = new PuloniaTaskHistoryViewModel(service, store);
        await model.RevealRunAsync(firstId);
        Assert.Equal(firstId, model.SelectedRun!.RequestId);

        await model.RefreshAsync(Guid.NewGuid());
        Assert.Null(model.SelectedRun);
        Assert.Null(model.SelectedRunNodeResult);
    }

    /// <summary>
    /// 各页签中新执行计划都使用当前配置和完整任务树，是否选中历史不会把新执行变成续跑。
    /// </summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task RunPlan_WithExistingHistoryCreatesFreshFullRun(bool keepHistorySelected, int tabIndex)
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var plan = CreatePlan("重新执行计划", "sample.sum");
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Name = "第二个计算节点", TaskType = "csharp",
            Parameters = new JObject { ["operation"] = "sample.sum", ["values"] = new JArray(4) }
        });
        plan = await store.SavePlanAsync(plan);
        var oldId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        var oldRun = await service.WaitForCompletionAsync(oldId).WaitAsync(TimeSpan.FromSeconds(20));
        var history = new PuloniaTaskHistoryViewModel(service, store);
        await history.RevealRunAsync(oldId);

        // 历史仍指向旧快照；当前计划更新后，新执行必须重新固定最新配置。
        plan.RootTask.Children[0].Parameters["values"] = new JArray(99);
        plan = await store.SavePlanAsync(plan);
        var clipboard = new PuloniaTaskClipboardService();
        var document = new PuloniaTaskPlanDocumentViewModel(plan, [], clipboard, isNew: false);
        var page = new PuloniaTaskPlanViewModel(store, clipboard, service, new PuloniaTaskResourceCatalog(), history)
        {
            SelectedDocument = document,
            SelectedPlanTabIndex = tabIndex
        };
        if (!keepHistorySelected)
        {
            history.SelectedRun = null;
            history.SelectedRunNodeResult = null;
        }
        Assert.Equal(keepHistorySelected, history.HasSelectedRun);
        Assert.True(page.RunPlanCommand.CanExecute(null));

        await page.RunPlanCommand.ExecuteAsync(null);
        var newId = history.SelectedRun!.RequestId;
        Assert.NotEqual(oldId, newId);
        var newRun = await service.WaitForCompletionAsync(newId).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, newRun.Status);
        Assert.Null(newRun.ResumedFromRunId);
        Assert.Null(newRun.ResumeFromTaskAddress);
        Assert.Null(newRun.TargetTaskId);
        Assert.Equal("ui", newRun.Source);
        Assert.Equal(2, newRun.NodeResults.Count);
        Assert.Equal(99, newRun.NodeResults[0].Data.Value<int>("sum"));
        Assert.Equal(4, newRun.NodeResults[1].Data.Value<int>("sum"));
        Assert.Equal(6, oldRun.NodeResults[0].Data.Value<int>("sum"));
        Assert.Equal(oldRun.SnapshotJson, (await service.GetRunAsync(oldId)).SnapshotJson);
        Assert.Equal(2, (await store.ListHistoryAsync()).Count);
    }

    /// <summary>卡片按钮使用传入的计划，不会执行仍在选中的其他计划或其历史。</summary>
    [Fact]
    public async Task PlanCardRun_UsesExplicitDocumentAndUnlimitedBudget()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var clipboard = new PuloniaTaskClipboardService();
        var selected = await store.SavePlanAsync(CreatePlan("选中计划", "sample.sum"));
        var card = CreatePlan("按钮所在计划", "sample.sum");
        card.RootTask.Children[0].Parameters["values"] = new JArray(37);
        card = await store.SavePlanAsync(card);
        var selectedDocument = new PuloniaTaskPlanDocumentViewModel(selected, [], clipboard, isNew: false);
        var cardDocument = new PuloniaTaskPlanDocumentViewModel(card, [], clipboard, isNew: false);
        var history = new PuloniaTaskHistoryViewModel(service, store);
        var page = new PuloniaTaskPlanViewModel(store, clipboard, service, new PuloniaTaskResourceCatalog(), history)
        { SelectedDocument = selectedDocument, SelectedPlanTabIndex = 1 };
        page.Documents.Add(selectedDocument);
        page.Documents.Add(cardDocument);
        Assert.True(page.RunPlanCommand.CanExecute(cardDocument));
        await page.RunPlanCommand.ExecuteAsync(cardDocument);
        var run = await service.WaitForCompletionAsync(history.SelectedRun!.RequestId).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(card.Id, run.PlanId);
        Assert.Equal(37, Assert.Single(run.NodeResults).Data.Value<int>("sum"));
        Assert.Null(run.ResumedFromRunId);
        Assert.Null(Assert.Single(await store.ListHistoryAsync()).Request.TimeoutSeconds);
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
        Guid requestId;
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
                Assert.Equal(requestId, model.SelectedRun!.RequestId);
                Assert.Equal("已停止", model.SelectedRun.StatusText);
                Assert.True(model.SelectedRun.IsHistorical);
                Assert.False(model.SelectedRun.CanCancel);
                Assert.Equal(PuloniaTaskRunStatus.Cancelled,
                    (await store.ListHistoryAsync()).Single(item => item.RequestId == requestId).Status);
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
        Guid requestId;
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
                    (await store.ListHistoryAsync()).Single(item => item.RequestId == requestId).Status);
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
        Assert.Equal("1 小时 40 分 0 秒", PuloniaTaskHistoryText.Duration(start, start.AddMinutes(100)));
    }

    /// <summary>
    /// 建立纯 C# 服务，游戏协调器的运行时永不使用，不启动游戏会话。
    /// </summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store,
        PuloniaCSharpTaskRegistry? registry = null, TaskStopService? stopService = null) => new(store,
        new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(registry ?? new PuloniaCSharpTaskRegistry())],
        new PuloniaGameTaskCoordinator(null!), stopService ?? new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>
    /// 建立受控计算或账本计划。
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
                Parameters = new JObject { ["operation"] = operation, ["values"] = new JArray(1, 2, 3), ["report_twice"] = true }
            }]
        }
    };

    /// <summary>
    /// 建立序列化必要字段齐全的历史样本。
    /// </summary>
    private static PuloniaTaskRunRecord CreateRecord() => new()
    {
        RequestId = Guid.NewGuid(), RunId = Guid.NewGuid(), Request = new PuloniaTaskRequest { PlanId = "history-test" },
        PlanName = "历史样本", SnapshotJson = "{}", Status = PuloniaTaskRunStatus.Succeeded,
        Message = "测试结束", SubmittedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// 建立无需访问资源的节点展示样本。
    /// </summary>
    private static PuloniaTaskNodeResultViewModel CreateNode(string taskType, JObject data) => new(
        new PuloniaTaskNodeResult("plan/node", "节点", taskType, 1, PuloniaTaskNodeStatus.Failed,
            "结果消息", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, data), null, taskType, []);

    /// <summary>
    /// 仅清理本测试生成的唯一临时目录，不接触真实程序数据。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
