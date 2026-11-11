using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 运行状态存储、历史与计划读取的健壮性回归：坏文件只跳过、备份按加载标志（按实例区分）保留、旧字段兼容、服务器时区来源；
/// 运行状态按实例存放、各实例独立的队列、运行 ID 查重共享历史、写锁短时重试。不启动应用或游戏。
/// </summary>
public sealed class PuloniaTaskRunStateTests : IDisposable
{
    /// <summary>本测试独占的临时目录，不接触程序真实 User/Pulonia。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>一份损坏的历史文件不影响启动和其他历史的查看；坏文件保留原样并通过恢复提示告知。</summary>
    [Fact]
    public async Task CorruptHistoryFile_IsSkippedWithoutBlockingStartup()
    {
        using var store = new PuloniaTaskStore(_directory);
        var plan = await store.SavePlanAsync(CreatePlan());
        PuloniaTaskRunView good;
        string badId;
        await using (var service = CreateService(store))
        {
            good = await RunAsync(service, plan);
            badId = (await RunAsync(service, plan)).RunId;
        }
        var badPath = Path.Combine(_directory, "history", plan.Id, badId + ".json");
        await File.WriteAllTextAsync(badPath, "{ 损坏");

        await using var restarted = CreateService(store);

        var listed = Assert.Single(await restarted.ListRunsAsync());
        Assert.Equal(good.RunId, listed.RunId);
        Assert.True(listed.IsHistorical);
        // 常驻历史不含快照正文，首次读取时从历史文件加载
        Assert.Equal(good.SnapshotJson, listed.SnapshotJson);
        Assert.Contains("无法读取", restarted.RecoveryNotice);
        Assert.Contains(badId, restarted.RecoveryNotice);
        Assert.Equal("{ 损坏", await File.ReadAllTextAsync(badPath));
        var history = await store.ListHistoryWithFailuresAsync();
        Assert.Equal(good.RunId, Assert.Single(history.Items).RunId);
        Assert.Equal(badId, Assert.Single(history.Failures).Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => restarted.GetRunAsync(badId));
        // 其余功能照常：续跑可按需读取完整历史，新运行也能提交
        var resumed = await restarted.WaitForCompletionAsync(await restarted.ResumeAsync(good.RunId)).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, resumed.Status);
        Assert.Equal(good.RunId, resumed.ResumedFromRunId);
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await RunAsync(restarted, plan)).Status);
    }

    /// <summary>一份坏计划只跳过它自己，列表仍返回其他计划，并给出可供界面提示的失败信息；文件不被删除。</summary>
    [Fact]
    public async Task CorruptPlanFile_IsSkippedAndReported()
    {
        using var store = new PuloniaTaskStore(_directory);
        var good = await store.SavePlanAsync(CreatePlan("正常计划"));
        var bad = await store.SavePlanAsync(CreatePlan("损坏计划"));
        var badPath = Path.Combine(_directory, "plans", bad.Id + ".json");
        await File.WriteAllTextAsync(badPath, "not json");

        Assert.Equal(good.Id, Assert.Single(await store.ListPlansAsync()).Id);
        var listed = await store.ListPlansWithFailuresAsync();
        Assert.Equal(good.Id, Assert.Single(listed.Items).Id);
        var failure = Assert.Single(listed.Failures);
        Assert.Equal(bad.Id, failure.Id);
        Assert.Equal(badPath, failure.Path);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
        Assert.True(File.Exists(badPath));
    }

    /// <summary>读取失败的计划无法判断触发器是否还在，调度检查保留其游标；不再报告读取失败后才按孤儿剔除。</summary>
    [Fact]
    public async Task CheckTriggers_KeepsCursorsOfUnreadablePlans()
    {
        using var store = new PuloniaTaskStore(_directory);
        var plan = CreatePlan();
        var trigger = new PuloniaTaskTrigger { Enabled = true, ActivatedAtUtc = DateTimeOffset.UtcNow.AddDays(-2) };
        plan.Triggers.Add(trigger);
        plan = await store.SavePlanAsync(plan);
        await store.SaveStateAsync(PuloniaInstanceKey.Primary, new PuloniaTaskState
        {
            TriggerStates = [new PuloniaTaskTriggerState { PlanId = plan.Id, TriggerId = trigger.Id,
                TriggerActivatedAtUtc = trigger.ActivatedAtUtc, LastOccurrenceUtc = DateTimeOffset.UtcNow.AddDays(-1) }]
        });
        await using var service = CreateService(store);

        await service.CheckTriggersAsync([], [plan.Id]);
        Assert.Equal(trigger.Id, Assert.Single((await store.LoadStateAsync(PuloniaInstanceKey.Primary)).State.TriggerStates).TriggerId);

        await service.CheckTriggersAsync([]);
        Assert.Empty((await store.LoadStateAsync(PuloniaInstanceKey.Primary)).State.TriggerStates);
    }

    /// <summary>正式状态文件损坏时从备份恢复，启动写回正式文件但不把损坏文件挪成备份；不需要回读旧文件判断。</summary>
    [Fact]
    public async Task CorruptPrimaryState_KeepsValidBackup()
    {
        Directory.CreateDirectory(_directory);
        // 主实例的运行状态在实例目录中
        var statePath = Path.Combine(_directory, "instances", "primary", "state.json");
        using (var writer = new PuloniaTaskStore(_directory))
        {
            // 两次保存后，第一次的内容成为备份
            await writer.SaveStateAsync(PuloniaInstanceKey.Primary, new PuloniaTaskState { Sequence = 7 });
            await writer.SaveStateAsync(PuloniaInstanceKey.Primary, new PuloniaTaskState { Sequence = 8 });
        }
        var backup = await File.ReadAllTextAsync(statePath + ".bak");
        await File.WriteAllTextAsync(statePath, "{ 损坏");

        using var store = new PuloniaTaskStore(_directory);
        await using (var service = CreateService(store))
        {
            Assert.Empty(await service.ListRunsAsync());
            Assert.Contains("备份", service.RecoveryNotice);
            Assert.Equal(backup, await File.ReadAllTextAsync(statePath + ".bak"));
            var primary = PuloniaTaskJson.ReadState(await File.ReadAllTextAsync(statePath));
            Assert.True(primary.Sequence > 7);
        }

        // 正式文件恢复有效后，保存重新按“上一版作为备份”替换
        var current = await File.ReadAllTextAsync(statePath);
        await store.SaveStateAsync(PuloniaInstanceKey.Primary, PuloniaTaskJson.ReadState(current));
        Assert.Equal(current, await File.ReadAllTextAsync(statePath + ".bak"));
    }

    /// <summary>运行状态按实例存放：只写本实例目录的 state.json，上一版保留为同目录的 .bak，不再写根目录。</summary>
    [Fact]
    public async Task State_IsStoredInInstanceDirectory()
    {
        using var store = new PuloniaTaskStore(_directory);
        await store.SaveStateAsync(PuloniaInstanceKey.ChildSession, new PuloniaTaskState { Sequence = 1 });
        await store.SaveStateAsync(PuloniaInstanceKey.ChildSession, new PuloniaTaskState { Sequence = 2 });

        var path = Path.Combine(_directory, "instances", "child_session", "state.json");
        Assert.Equal(path, store.GetStatePath(PuloniaInstanceKey.ChildSession));
        Assert.Equal(2, PuloniaTaskJson.ReadState(await File.ReadAllTextAsync(path)).Sequence);
        Assert.Equal(1, PuloniaTaskJson.ReadState(await File.ReadAllTextAsync(path + ".bak")).Sequence);
        Assert.False(File.Exists(Path.Combine(_directory, "state.json")));
        Assert.False(File.Exists(store.GetStatePath(PuloniaInstanceKey.Primary)));
        Assert.Equal(2, (await store.LoadStateAsync(PuloniaInstanceKey.ChildSession)).State.Sequence);
        // 其他实例没有状态文件时读到空状态
        Assert.Equal(0, (await store.LoadStateAsync(PuloniaInstanceKey.Primary)).State.Sequence);
    }

    /// <summary>备份保留标志按实例的状态文件区分：主实例从备份恢复后，其他实例的保存仍按“上一版作为备份”替换。</summary>
    [Fact]
    public async Task BackupPreservation_IsTrackedPerInstance()
    {
        using var store = new PuloniaTaskStore(_directory);
        const string child = "webview:小号a";
        await store.SaveStateAsync(PuloniaInstanceKey.Primary, new PuloniaTaskState { Sequence = 1 });
        await store.SaveStateAsync(PuloniaInstanceKey.Primary, new PuloniaTaskState { Sequence = 2 });
        await store.SaveStateAsync(child, new PuloniaTaskState { Sequence = 10 });
        await store.SaveStateAsync(child, new PuloniaTaskState { Sequence = 11 });
        var primaryPath = store.GetStatePath(PuloniaInstanceKey.Primary);
        var childPath = store.GetStatePath(child);
        await File.WriteAllTextAsync(primaryPath, "{ 损坏");

        // 1. 主实例正式文件损坏：从备份恢复，并记住它的下一次保存要保留这份备份
        var recovered = await store.LoadStateAsync(PuloniaInstanceKey.Primary);
        Assert.True(recovered.RecoveredFromBackup);
        Assert.Equal(1, recovered.State.Sequence);
        // 2. 其他实例照常把上一版轮换为备份
        await store.SaveStateAsync(child, new PuloniaTaskState { Sequence = 12 });
        Assert.Equal(11, PuloniaTaskJson.ReadState(await File.ReadAllTextAsync(childPath + ".bak")).Sequence);
        // 3. 主实例的下一次保存不把损坏文件挪成备份
        await store.SaveStateAsync(PuloniaInstanceKey.Primary, new PuloniaTaskState { Sequence = 3 });
        Assert.Equal(1, PuloniaTaskJson.ReadState(await File.ReadAllTextAsync(primaryPath + ".bak")).Sequence);
        Assert.Equal(3, PuloniaTaskJson.ReadState(await File.ReadAllTextAsync(primaryPath)).Sequence);
    }

    /// <summary>
    /// 两个实例的协调器各自读写自己的运行状态、各自消费队列：一个实例的运行未结束时另一个实例照常执行，
    /// 一个实例的游标与活动运行不出现在另一个实例的状态中，也不能从另一个实例取消；历史目录共用。
    /// </summary>
    [Fact]
    public async Task TwoInstances_UseSeparateStateAndQueues()
    {
        // 每个实例在自己的进程中有自己的存储对象，这里各建一个指向同一目录
        using var primaryStore = new PuloniaTaskStore(_directory);
        using var childStore = new PuloniaTaskStore(_directory);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var primaryRegistry = new PuloniaCSharpTaskRegistry();
        primaryRegistry.Register("test.wait", async (_, _, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return PuloniaTaskOutcome.Success("主实例完成");
        });
        var waitPlan = await primaryStore.SavePlanAsync(CreateOperationPlan("test.wait"));
        var sumPlan = await primaryStore.SavePlanAsync(CreatePlan());
        // 主实例已有一条定时游标，子实例的状态中不应出现
        await primaryStore.SaveStateAsync(PuloniaInstanceKey.Primary, new PuloniaTaskState
        {
            TriggerStates = [new PuloniaTaskTriggerState { PlanId = waitPlan.Id, TriggerId = PuloniaId.NewTriggerId(),
                TriggerActivatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1), LastOccurrenceUtc = DateTimeOffset.UtcNow.AddHours(-1) }]
        });
        await using var primary = CreateService(primaryStore, primaryRegistry, PuloniaInstanceKey.Primary);
        await using var child = CreateService(childStore, new PuloniaCSharpTaskRegistry(), PuloniaInstanceKey.ChildSession);

        // 1. 主实例的运行进入执行并保持未结束
        var primaryId = await primary.EnqueueAsync(new PuloniaTaskRequest { PlanId = waitPlan.Id });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        // 2. 子实例的运行不在主实例的队列中排队，照常执行完成
        var childRun = await child.WaitForCompletionAsync(await child.EnqueueAsync(new PuloniaTaskRequest { PlanId = sumPlan.Id }))
            .WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, childRun.Status);
        Assert.Equal(PuloniaTaskRunStatus.Running, (await primary.GetRunAsync(primaryId)).Status);
        // 3. 各自的状态文件互不包含对方的内容；取消只针对本实例的运行
        var childState = (await childStore.LoadStateAsync(PuloniaInstanceKey.ChildSession)).State;
        Assert.Null(childState.ActiveRun);
        Assert.Empty(childState.TriggerStates);
        var primaryState = (await primaryStore.LoadStateAsync(PuloniaInstanceKey.Primary)).State;
        Assert.Equal(primaryId, primaryState.ActiveRun!.RunId);
        Assert.Single(primaryState.TriggerStates);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => child.CancelAsync(primaryId));

        // 4. 主实例结束后，两次运行都写入共用的历史目录
        release.SetResult();
        Assert.Equal(PuloniaTaskRunStatus.Succeeded,
            (await primary.WaitForCompletionAsync(primaryId).WaitAsync(TimeSpan.FromSeconds(20))).Status);
        Assert.Equal(new[] { primaryId, childRun.RunId }.Order(),
            (await primaryStore.ListHistoryAsync()).Select(item => item.RunId).Order());
    }

    /// <summary>生成运行 ID 时检查共享历史目录：其他实例已写入同名历史文件时换一个 ID，不与它冲突。</summary>
    [Fact]
    public async Task RunId_SkipsIdAlreadyInSharedHistory()
    {
        using var store = new PuloniaTaskStore(_directory);
        var plan = await store.SavePlanAsync(CreatePlan());
        var now = DateTimeOffset.Now;
        var taken = PuloniaId.NewRunId(now);
        var fresh = PuloniaId.NewRunId(now);
        while (fresh == taken)
            fresh = PuloniaId.NewRunId(now);
        var candidates = new Queue<string>([taken, fresh]);
        await using var service = new PuloniaTaskService(store, new PuloniaTaskBuilder(store),
            [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance)) { RunIdGenerator = _ => candidates.Dequeue() };
        // 服务启动后才由“其他实例”写入的历史不在内存中，只能从历史目录发现
        Assert.Empty(await service.ListRunsAsync());
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(plan, new PuloniaTaskBuildOptions
            { Definitions = new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry()).Definitions.ToList() });
        await store.ArchiveRunAsync(new PuloniaTaskRunRecord
        {
            RunId = taken, PlanName = plan.Name, SnapshotJson = snapshot.ToJson(), Status = PuloniaTaskRunStatus.Succeeded,
            Message = "其他实例的运行", SubmittedAt = now, FinishedAt = now, Request = new PuloniaTaskRequest { PlanId = plan.Id }
        });
        Assert.True(store.HistoryExists(plan.Id, taken));

        var id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });

        Assert.Equal(fresh, id);
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(20))).Status);
        Assert.Equal("其他实例的运行", (await store.LoadHistoryRecordAsync(plan.Id, taken))!.Message);
    }

    /// <summary>写锁被其他实例短暂占用时，写入按间隔重试而不是立即失败；占用解除后写入成功。</summary>
    [Fact]
    public async Task WriterLock_BrieflyHeldElsewhere_IsAcquiredAfterRelease()
    {
        using var store = new PuloniaTaskStore(_directory);
        Directory.CreateDirectory(_directory);
        var lockPath = Path.Combine(_directory, ".writer.lock");
        Task<PuloniaTaskPlan> saving;
        Task<FileStream> acquiring;
        // 模拟另一个实例持有写锁
        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            saving = store.SavePlanAsync(CreatePlan());
            await Task.Delay(300);
            Assert.False(saving.IsCompleted);
        }
        Assert.Equal(1, (await saving.WaitAsync(TimeSpan.FromSeconds(10))).Revision);

        using (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            acquiring = store.AcquireWriterLockAsync();
            await Task.Delay(200);
            Assert.False(acquiring.IsCompleted);
        }
        await using var acquired = await acquiring.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(acquired.CanWrite);
    }

    /// <summary>新运行的服务器时区由入队时的配置统一填写，调用方的取值被覆盖；续跑沿用原请求的取值。</summary>
    [Fact]
    public async Task ServerUtcOffset_ComesFromServerTimeAndResumeKeepsOriginal()
    {
        using var store = new PuloniaTaskStore(_directory);
        var plan = await store.SavePlanAsync(CreatePlan());
        // 历史中的原运行按 UTC-5 固定，且有未完成节点可以续跑
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(plan, new PuloniaTaskBuildOptions
            { Definitions = new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry()).Definitions.ToList() });
        var now = DateTimeOffset.UtcNow;
        var original = new PuloniaTaskRunRecord
        {
            RunId = PuloniaId.NewRunId(now.AddMinutes(-5)), PlanName = plan.Name, SnapshotJson = snapshot.ToJson(),
            Status = PuloniaTaskRunStatus.Failed, Message = "失败", SubmittedAt = now.AddMinutes(-5), FinishedAt = now.AddMinutes(-4),
            Request = new PuloniaTaskRequest { PlanId = plan.Id, ServerUtcOffsetMinutes = -300 }
        };
        await store.ArchiveRunAsync(original);
        await using var service = CreateService(store);

        var fresh = await service.WaitForCompletionAsync(await service.EnqueueAsync(
            new PuloniaTaskRequest { PlanId = plan.Id, ServerUtcOffsetMinutes = 60 })).WaitAsync(TimeSpan.FromSeconds(20));
        var resumed = await service.WaitForCompletionAsync(await service.ResumeAsync(original.RunId)).WaitAsync(TimeSpan.FromSeconds(20));

        var history = await store.ListHistoryAsync();
        Assert.Equal(PuloniaServerTime.GetUtcOffsetMinutes(), history.Single(item => item.RunId == fresh.RunId).Request.ServerUtcOffsetMinutes);
        Assert.Equal(-300, history.Single(item => item.RunId == resumed.RunId).Request.ServerUtcOffsetMinutes);
    }

    /// <summary>游戏日按服务器时区凌晨 4 点切换，供路线 CD 的日、周边界使用。</summary>
    [Theory]
    [InlineData("2026-10-12T03:59:59+08:00", 8, "2026-10-11")]
    [InlineData("2026-10-12T04:00:00+08:00", 8, "2026-10-12")]
    [InlineData("2026-10-12T08:30:00+00:00", -5, "2026-10-11")]
    [InlineData("2026-10-12T09:00:00+00:00", -5, "2026-10-12")]
    [InlineData("2026-10-12T08:30:00+00:00", 1, "2026-10-12")]
    [InlineData("2026-10-12T02:30:00+00:00", 1, "2026-10-11")]
    public void GameDate_SwitchesAtFourInServerTime(string instant, int offsetHours, string expected)
        => Assert.Equal(DateTime.Parse(expected), PuloniaServerTime.GetGameDate(DateTimeOffset.Parse(instant), TimeSpan.FromHours(offsetHours)));

    /// <summary>建立只执行 C# 求和的计划。</summary>
    private static PuloniaTaskPlan CreatePlan(string name = "运行状态测试") => new()
    {
        Name = name,
        RootTask = new PuloniaTask
        {
            Name = "根", Children = [new PuloniaTask { Name = "计算", TaskType = "csharp",
                Parameters = new JObject { ["operation"] = "sample.sum", ["values"] = new JArray(1, 2) } }]
        }
    };

    /// <summary>建立只有一个指定 C# 操作节点的计划。</summary>
    private static PuloniaTaskPlan CreateOperationPlan(string operation) => new()
    {
        Name = "操作 " + operation,
        RootTask = new PuloniaTask
        {
            Name = "根", Children = [new PuloniaTask { Name = "操作", TaskType = "csharp",
                Parameters = new JObject { ["operation"] = operation } }]
        }
    };

    /// <summary>建立指定实例键的纯 C# 服务，模拟该实例进程中的协调器。</summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store, PuloniaCSharpTaskRegistry registry, string instanceKey)
        => new(store, new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(registry)], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance),
            accountService: new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(instanceKey)));

    /// <summary>提交并等待一次运行完成，避免无限等待。</summary>
    private static async Task<PuloniaTaskRunView> RunAsync(PuloniaTaskService service, PuloniaTaskPlan plan)
        => await service.WaitForCompletionAsync(await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id }))
            .WaitAsync(TimeSpan.FromSeconds(20));

    /// <summary>建立纯 C# 服务，不启动游戏会话。</summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store) => new(store, new PuloniaTaskBuilder(store),
        [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())], new PuloniaGameTaskCoordinator(null!),
        new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>只清理本测试的独占临时目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
