using System.Windows.Input;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>日程、配置表单与耐久调度回归；不启动应用或游戏。</summary>
public sealed class PuloniaTaskTriggerTests : IDisposable
{
    /// <summary>本测试独占临时目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>常见日周月和数字 Cron 的下次发生采用明确时区。</summary>
    [Theory]
    [InlineData("0 4 * * *", "2026-10-04T03:59:00+08:00", "2026-10-04T04:00:00+08:00")]
    [InlineData("0 4 * * 1", "2026-10-04T04:00:00+08:00", "2026-10-05T04:00:00+08:00")]
    [InlineData("0 4 31 * *", "2026-04-01T00:00:00+08:00", "2026-05-31T04:00:00+08:00")]
    [InlineData("*/15 4,20 * * *", "2026-10-04T04:45:00+08:00", "2026-10-04T20:00:00+08:00")]
    [InlineData("0 4 * * 7", "2026-10-03T12:00:00+08:00", "2026-10-04T04:00:00+08:00")]
    public void Cron_NextUsesCalendarAndTimezone(string cron, string after, string expected)
        => Assert.Equal(DateTimeOffset.Parse(expected).ToUniversalTime(), new PuloniaCronExpression(cron)
            .Next(DateTimeOffset.Parse(after), TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")));

    /// <summary>触发器只提供三个原神服务器时区，默认使用 UTC+8 固定偏移。</summary>
    [Fact]
    public void ServerTimeZones_AreLimitedAndDefaultToUtcPlus8()
    {
        var trigger = new PuloniaTaskTrigger();
        var editor = new PuloniaTaskTriggerEditorViewModel(trigger, Plan());

        Assert.Equal(PuloniaTaskServerTimeZone.UtcPlus8, trigger.ServerTimeZone);
        Assert.Equal(3, editor.ServerTimeZones.Count);
        Assert.Equal(TimeSpan.FromHours(8), PuloniaTaskSchedule.GetTimeZone(trigger.ServerTimeZone).BaseUtcOffset);
        Assert.Equal(TimeSpan.FromHours(1), PuloniaTaskSchedule.GetTimeZone(PuloniaTaskServerTimeZone.Europe).BaseUtcOffset);
        Assert.Equal(TimeSpan.FromHours(-5), PuloniaTaskSchedule.GetTimeZone(PuloniaTaskServerTimeZone.America).BaseUtcOffset);
        Assert.Throws<FormatException>(() => PuloniaTaskSchedule.Validate(new PuloniaTaskTrigger
            { ServerTimeZone = (PuloniaTaskServerTimeZone)0 }));
    }

    /// <summary>不接受 Quartz、越界字段及零步长。</summary>
    [Theory]
    [InlineData("0 0 4 * * *")]
    [InlineData("60 4 * * *")]
    [InlineData("0 24 * * *")]
    [InlineData("*/0 4 * * *")]
    [InlineData("0 4 ? * 1")]
    [InlineData("0 4 L * *")]
    [InlineData("0 4 * * MON")]
    [InlineData("0, 4 * * *")]
    public void Cron_RejectsUnsupportedDialect(string cron) => Assert.Throws<FormatException>(() => new PuloniaCronExpression(cron));

    /// <summary>DST 缺失顺延，重复时刻只取第一次，Latest 与 Next 使用相同解释。</summary>
    [Fact]
    public void Cron_DstGapAndFoldDoNotDuplicate()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T07:00:00Z"), new PuloniaCronExpression("30 2 * * *")
            .Next(DateTimeOffset.Parse("2026-03-08T05:00:00Z"), zone));
        var fold = new PuloniaCronExpression("30 1 * * *");
        var first = DateTimeOffset.Parse("2026-11-01T05:30:00Z");
        Assert.Equal(first, fold.Next(first.AddHours(-2), zone));
        Assert.Equal(DateTimeOffset.Parse("2026-11-02T06:30:00Z"), fold.Next(first, zone));
        Assert.Equal(first, fold.Latest(first.AddHours(1), first.AddHours(-2), zone));
    }

    /// <summary>日与星期限制取交集，不把“月1号且周一”解释成每周一。</summary>
    [Fact]
    public void Cron_DayAndWeekdayUseIntersection()
        => Assert.Equal(DateTimeOffset.Parse("2026-06-01T04:00:00Z"), new PuloniaCronExpression("0 4 1 * 1")
            .Next(DateTimeOffset.Parse("2026-04-02T00:00:00Z"), TimeZoneInfo.Utc));

    /// <summary>直接编辑表达式后，保存和校验始终读取同一份 Cron。</summary>
    [Fact]
    public void Editor_DirectExpressionIsSingleScheduleSource()
    {
        var editor = new PuloniaTaskTriggerEditorViewModel(new PuloniaTaskTrigger(), Plan());
        editor.CronEditor.Expression = "*/20 1,13 * * *";
        Assert.Equal("*/20 1,13 * * *", editor.CreateTrigger().Cron);

        editor.CronEditor.Expression = "0 25 * * *";
        Assert.False(editor.IsValid);
        Assert.Contains("无效", editor.PreviewText);
    }

    /// <summary>常用 Cron 预设必须直接写入可保存的标准五段表达式。</summary>
    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("*/15 * * * *")]
    [InlineData("0 * * * *")]
    [InlineData("0 4 * * *")]
    [InlineData("0 9 * * 1-5")]
    [InlineData("0 4 * * 1")]
    [InlineData("0 4 1 * *")]
    public void CronEditor_PresetsWriteExpression(string cron)
    {
        var editor = new PuloniaCronEditorViewModel("0 4 * * *") { IsPopupOpen = true };
        editor.ApplyPresetCommand.Execute(cron);
        Assert.Equal(cron, editor.Expression);
        Assert.False(editor.IsPopupOpen);
    }

    /// <summary>周期生成器只使用能整除时间边界的周期，并生成对应步长表达式。</summary>
    [Theory]
    [InlineData("minute", 15, 1, 0, "*/15 * * * *")]
    [InlineData("hour", 5, 6, 30, "30 */6 * * *")]
    public void CronEditor_PeriodGeneratorWritesExpression(string unit, int minuteInterval,
        int hourInterval, int startMinute, string cron)
    {
        var editor = new PuloniaCronEditorViewModel("0 4 * * *")
        {
            GeneratorTabIndex = 0,
            PeriodUnit = unit,
            MinuteInterval = minuteInterval,
            HourInterval = hourInterval,
            HourStartMinute = startMinute
        };
        editor.ApplyGeneratedExpressionCommand.Execute(null);
        Assert.Equal(cron, editor.Expression);
    }

    /// <summary>固定时间生成器覆盖每天、工作日、每周和每月四种常见日程。</summary>
    [Theory]
    [InlineData("daily", 1, 1, "15 17 * * *")]
    [InlineData("weekdays", 1, 1, "15 17 * * 1-5")]
    [InlineData("weekly", 3, 1, "15 17 * * 3")]
    [InlineData("monthly", 1, 20, "15 17 20 * *")]
    public void CronEditor_FixedTimeGeneratorWritesExpression(string frequency, int weekday,
        int monthDay, string cron)
    {
        var editor = new PuloniaCronEditorViewModel("0 4 * * *")
        {
            GeneratorTabIndex = 1,
            FixedFrequency = frequency,
            FixedHour = 17,
            FixedMinute = 15,
            FixedWeekday = weekday,
            FixedMonthDay = monthDay
        };
        editor.ApplyGeneratedExpressionCommand.Execute(null);
        Assert.Equal(cron, editor.Expression);
    }

    /// <summary>到点立即提交请求，重启及回拨都不会再次执行同一发生时间。</summary>
    [Fact]
    public async Task Scheduler_RunsImmediatelyAndDeduplicatesAcrossRestart()
    {
        using var store = new PuloniaTaskStore(_directory);
        var clock = new TestClock();
        var plan = Plan(); plan.Triggers.Add(Trigger(clock));
        plan = await store.SavePlanAsync(plan);
        string id;
        await using (var service = Service(store, clock))
        {
            await service.CheckTriggersAsync([plan]);
            id = Assert.Single(await service.ListRunsAsync()).RunId;
            Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(15))).Status);
            await service.CheckTriggersAsync([plan]);
            Assert.Single(await service.ListRunsAsync());
            var persisted = await store.LoadStateAsync();
            Assert.Equal(id, Assert.Single(persisted.State.TriggerStates).LastRunId);
            Assert.Equal("已完成", Assert.Single(persisted.State.TriggerStates).Status);
        }
        await using var restarted = Service(store, clock);
        await restarted.CheckTriggersAsync([plan]);
        clock.Advance(TimeSpan.FromMinutes(-20));
        await restarted.CheckTriggersAsync([plan]);
        Assert.Single(await restarted.ListRunsAsync());
        Assert.Equal(id, Assert.Single(await restarted.ListTriggerStatesAsync()).LastRunId);
    }

    /// <summary>离线多日只合并窗口内最新一次，关闭补触发时不会运行旧时间。</summary>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task Scheduler_CatchUpIsBounded(bool catchUp, int expected)
    {
        using var store = new PuloniaTaskStore(_directory);
        var clock = new TestClock(); clock.Advance(TimeSpan.FromHours(2));
        var plan = Plan(); var trigger = Trigger(clock); trigger.ActivatedAtUtc = clock.GetUtcNow().AddDays(-20);
        trigger.CatchUp = catchUp; plan.Triggers.Add(trigger); plan = await store.SavePlanAsync(plan);
        await using var service = Service(store, clock);
        await service.CheckTriggersAsync([plan]);
        var runs = await service.ListRunsAsync();
        Assert.Equal(expected, runs.Count);
        if (runs.Count > 0) Assert.Equal(clock.GetUtcNow().AddHours(-2).AddSeconds(-2), runs[0].OccurrenceUtc);
    }

    /// <summary>排队后再次复核：到期或禁用时不启动任何节点，并保存可查看的历史。</summary>
    [Theory]
    [InlineData(false, PuloniaTaskRunStatus.Expired)]
    [InlineData(true, PuloniaTaskRunStatus.Cancelled)]
    public async Task Queue_RechecksDeadlineAndConfiguration(bool disable, PuloniaTaskRunStatus expected)
    {
        using var store = new PuloniaTaskStore(_directory);
        var clock = new TestClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new PuloniaCSharpTaskRegistry();
        registry.Register("test.block", async (_, _, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); return PuloniaTaskOutcome.Success("退出"); });
        var blocker = Plan("test.block"); blocker = await store.SavePlanAsync(blocker);
        var plan = Plan(); plan.Triggers.Add(Trigger(clock)); plan = await store.SavePlanAsync(plan);
        await using var service = Service(store, clock, registry);
        await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = blocker.Id });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await service.CheckTriggersAsync([plan]);
        var id = (await service.ListRunsAsync()).Single(item => item.PlanId == plan.Id).RunId;
        var durable = (await store.LoadStateAsync()).State;
        Assert.Equal(id, durable.TriggerStates.Single(item => item.TriggerId == plan.Triggers[0].Id).LastRunId);
        Assert.Contains(durable.PendingRequests, item => item.RunId == id);
        if (disable) { plan.Triggers[0].Enabled = false; await store.SavePlanAsync(plan); }
        else clock.Advance(TimeSpan.FromHours(7));
        release.SetResult();
        var result = await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(expected, result.Status); Assert.Null(result.StartedAt); Assert.Empty(result.NodeResults);
        Assert.Contains(await store.ListHistoryAsync(), item => item.RunId == id && item.Request.TriggerId == plan.Triggers[0].Id);
    }

    /// <summary>子树范围不准备被排除资源，同时保留目标稳定地址。</summary>
    [Fact]
    public async Task Builder_SubtreeDoesNotReadOtherResources()
    {
        using var store = new PuloniaTaskStore(_directory);
        var plan = Plan();
        var target = plan.RootTask.Children[0];
        plan.RootTask.Children.Add(new PuloniaTask { Name = "无关资源", TaskType = "pathing", Path = "User/AutoPathing/not-found.json" });
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(plan, new PuloniaTaskBuildOptions
        { TargetTaskId = target.Id, Definitions = new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry()).Definitions.ToList() });
        Assert.True(snapshot.RootTask.Children[0].IsEnabled);
        Assert.False(snapshot.RootTask.Children[1].IsEnabled);
        Assert.EndsWith("/" + target.Id, snapshot.RootTask.Children[0].TaskAddress);
    }

    /// <summary>恢复已持久化的排队请求时必须可消费，不能再次提交同一 occurrence。</summary>
    [Fact]
    public async Task Restart_RestoresDurableQueueWithCursor()
    {
        using var store = new PuloniaTaskStore(_directory);
        var clock = new TestClock();
        var plan = Plan(); var trigger = Trigger(clock); plan.Triggers.Add(trigger); plan = await store.SavePlanAsync(plan);
        var id = PuloniaId.NewRunId(clock.GetUtcNow()); var occurrence = clock.GetUtcNow().AddSeconds(-2);
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(plan, new PuloniaTaskBuildOptions
        { Definitions = new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry()).Definitions.ToList() });
        await store.SaveStateAsync(new PuloniaTaskState
        {
            PendingRequests = [new PuloniaTaskRunRecord
            {
                RunId = id, PlanName = plan.Name, Message = "已排队", Status = PuloniaTaskRunStatus.Queued,
                SnapshotJson = PuloniaTaskJson.Write(snapshot), SubmittedAt = clock.GetUtcNow(),
                Request = new PuloniaTaskRequest { PlanId = plan.Id, Source = "schedule", TriggerId = trigger.Id,
                    TriggerActivatedAtUtc = trigger.ActivatedAtUtc, OccurrenceUtc = occurrence, DeadlineUtc = occurrence.AddHours(6) }
            }],
            TriggerStates = [new PuloniaTaskTriggerState { PlanId = plan.Id, TriggerId = trigger.Id,
                TriggerActivatedAtUtc = trigger.ActivatedAtUtc, LastOccurrenceUtc = occurrence, LastRunId = id,
                NextOccurrenceUtc = PuloniaTaskSchedule.Next(trigger, occurrence) }]
        });
        await using var service = Service(store, clock);
        await service.CheckTriggersAsync([plan]);
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(15))).Status);
        Assert.Single(await service.ListRunsAsync());
    }

    /// <summary>同计划忙碌策略有界；多个遗漏只允许保留一个排队候选。</summary>
    [Theory]
    [InlineData(PuloniaTaskBusyPolicy.Skip, 1)]
    [InlineData(PuloniaTaskBusyPolicy.QueueOnce, 2)]
    public async Task BusyPolicy_DoesNotAccumulateUnboundedQueue(PuloniaTaskBusyPolicy policy, int expected)
    {
        using var store = new PuloniaTaskStore(_directory);
        var clock = new TestClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registry = new PuloniaCSharpTaskRegistry();
        registry.Register("test.block", async (_, _, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); return PuloniaTaskOutcome.Success("退出"); });
        var plan = Plan("test.block"); var trigger = Trigger(clock);
        trigger.BusyPolicy = policy;
        trigger.Cron = "* * * * *";
        plan.Triggers.Add(trigger); plan = await store.SavePlanAsync(plan);
        await using var service = Service(store, clock, registry);
        await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        for (var i = 0; i < 3; i++) { await service.CheckTriggersAsync([plan]); clock.Advance(TimeSpan.FromMinutes(1)); }
        Assert.Equal(expected, (await service.ListRunsAsync()).Count);
        release.SetResult();
    }

    /// <summary>停止当前只请求取消，新任务必须等旧执行器 finally 清理完成才能开始。</summary>
    [Fact]
    public async Task StopCurrent_WaitsForExecutorCleanup()
    {
        using var store = new PuloniaTaskStore(_directory);
        var clock = new TestClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false; var registry = new PuloniaCSharpTaskRegistry();
        registry.Register("test.block", async (_, _, ct) =>
        {
            entered.SetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return PuloniaTaskOutcome.Success("不会到达"); }
            finally { await Task.Delay(50); cleaned = true; }
        });
        registry.Register("test.after", (_, _, _) => { Assert.True(cleaned); return Task.FromResult(PuloniaTaskOutcome.Success("已清理")); });
        var blocker = await store.SavePlanAsync(Plan("test.block"));
        var plan = Plan("test.after"); var trigger = Trigger(clock); trigger.BusyPolicy = PuloniaTaskBusyPolicy.StopCurrent;
        plan.Triggers.Add(trigger); plan = await store.SavePlanAsync(plan);
        await using var service = Service(store, clock, registry);
        var old = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = blocker.Id });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); await service.CheckTriggersAsync([plan]);
        var next = (await service.ListRunsAsync()).Single(item => item.PlanId == plan.Id).RunId;
        Assert.Equal(PuloniaTaskRunStatus.Cancelled, (await service.WaitForCompletionAsync(old).WaitAsync(TimeSpan.FromSeconds(15))).Status);
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await service.WaitForCompletionAsync(next).WaitAsync(TimeSpan.FromSeconds(15))).Status);
    }

    /// <summary>全局热键主动连按由防抖和同计划忙碌策略合并，不依赖真实热键注册。</summary>
    [Fact]
    public async Task Hotkey_RepeatedEventDoesNotDuplicate()
    {
        using var store = new PuloniaTaskStore(_directory);
        var clock = new TestClock(); var plan = Plan(); var trigger = Trigger(clock);
        trigger.Kind = PuloniaTaskTriggerKind.Hotkey; plan.Triggers.Add(trigger); plan = await store.SavePlanAsync(plan);
        await using var service = Service(store, clock);
        await service.FireHotkeyAsync(plan.Id, trigger.Id); await service.FireHotkeyAsync(plan.Id, trigger.Id);
        Assert.Single(await service.ListRunsAsync());
    }

    /// <summary>关闭离线补触发时，到点立即运行且不会在后续扫描重复提交。</summary>
    [Fact]
    public async Task NoCatchUp_OnTimeOccurrenceRunsImmediately()
    {
        using var store = new PuloniaTaskStore(_directory);
        var clock = new TestClock();
        var plan = Plan(); var trigger = Trigger(clock); trigger.CatchUp = false;
        plan.Triggers.Add(trigger); plan = await store.SavePlanAsync(plan);
        await using var service = Service(store, clock);
        await service.CheckTriggersAsync([plan]);
        var id = Assert.Single(await service.ListRunsAsync()).RunId;
        Assert.Equal(PuloniaTaskRunStatus.Succeeded,
            (await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(15))).Status);
        clock.Advance(TimeSpan.FromMinutes(10));
        await service.CheckTriggersAsync([plan]);
        Assert.Single(await service.ListRunsAsync());
    }

    /// <summary>只清理本测试生成且校验过的独占临时目录。</summary>
    public void Dispose()
    {
        var path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(path).StartsWith("bgi-", StringComparison.Ordinal))
            throw new InvalidOperationException("测试清理目标越界。");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    /// <summary>建立不依赖游戏的可运行计划。</summary>
    private static PuloniaTaskPlan Plan(string operation = "sample.sum") => new()
    { Name = "触发测试", RootTask = new PuloniaTask { Name = "根", Children = [new PuloniaTask
        { Name = "计算", TaskType = "csharp", Parameters = new JObject { ["operation"] = operation, ["values"] = new JArray(1, 2) } }] } };

    /// <summary>建立已启用且覆盖当前发生时刻的日程。</summary>
    private static PuloniaTaskTrigger Trigger(TestClock clock) => new()
    { Enabled = true, ActivatedAtUtc = clock.GetUtcNow().AddHours(-1) };

    /// <summary>建立仅使用受控 C# 执行器的调度服务。</summary>
    private static PuloniaTaskService Service(PuloniaTaskStore store, TestClock clock,
        PuloniaCSharpTaskRegistry? registry = null) => new(store, new PuloniaTaskBuilder(store),
        [new PuloniaCSharpTaskExecutor(registry ?? new PuloniaCSharpTaskRegistry())],
        new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance), clock);

    /// <summary>可回拨的 UTC 测试时钟，计时器仍使用真实有界等待。</summary>
    private sealed class TestClock : TimeProvider
    {
        /// <summary>测试时间戳，原子读写。</summary>
        private long _ticks = DateTimeOffset.Parse("2026-10-04T04:00:02+08:00").UtcTicks;
        /// <summary>读取当前测试时间。</summary>
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        /// <summary>推进或回拨测试时间。</summary>
        public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
    }

}
