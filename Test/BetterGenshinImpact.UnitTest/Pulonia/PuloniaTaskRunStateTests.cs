using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 运行状态存储、历史与计划读取的健壮性回归：坏文件只跳过、备份按加载标志保留、旧字段兼容、服务器时区来源。
/// 不启动应用或游戏。
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
        await store.SaveStateAsync(new PuloniaTaskState
        {
            TriggerStates = [new PuloniaTaskTriggerState { PlanId = plan.Id, TriggerId = trigger.Id,
                TriggerActivatedAtUtc = trigger.ActivatedAtUtc, LastOccurrenceUtc = DateTimeOffset.UtcNow.AddDays(-1) }]
        });
        await using var service = CreateService(store);

        await service.CheckTriggersAsync([], [plan.Id]);
        Assert.Equal(trigger.Id, Assert.Single((await store.LoadStateAsync()).State.TriggerStates).TriggerId);

        await service.CheckTriggersAsync([]);
        Assert.Empty((await store.LoadStateAsync()).State.TriggerStates);
    }

    /// <summary>正式状态文件损坏时从备份恢复，启动写回正式文件但不把损坏文件挪成备份；不需要回读旧文件判断。</summary>
    [Fact]
    public async Task CorruptPrimaryState_KeepsValidBackup()
    {
        Directory.CreateDirectory(_directory);
        var statePath = Path.Combine(_directory, "state.json");
        using (var writer = new PuloniaTaskStore(_directory))
        {
            // 两次保存后，第一次的内容成为备份
            await writer.SaveStateAsync(new PuloniaTaskState { Sequence = 7 });
            await writer.SaveStateAsync(new PuloniaTaskState { Sequence = 8 });
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
        await store.SaveStateAsync(PuloniaTaskJson.ReadState(current));
        Assert.Equal(current, await File.ReadAllTextAsync(statePath + ".bak"));
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

    /// <summary>游戏日按服务器时区凌晨 4 点切换，采集 CD 与每周安排共用。</summary>
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
