using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 触发器的执行实例（统筹设计 6.4）：定时由执行实例各自检查并在本地提交，游标存在各自的 state.json；
/// 计划热键只在主实例生效，不能选择执行实例。另含实例目录名与实例键的转换。不启动应用或游戏。
/// </summary>
public sealed class PuloniaTriggerInstanceTests : IDisposable
{
    /// <summary>云原神实例 a 的实例键。</summary>
    private const string KeyA = "webview:a";

    /// <summary>等待运行结束的上限。</summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(20);

    /// <summary>本测试独占的临时目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>执行实例为空时只有主实例负责定时；列出执行实例时只有它们负责；热键触发器不由任何实例按定时处理。</summary>
    [Fact] public void ScheduleIsCheckedByListedInstancesOnly()
    {
        var primaryOnly = new PuloniaTaskTrigger();
        Assert.True(primaryOnly.IsScheduledOn(PuloniaInstanceKey.Primary));
        Assert.False(primaryOnly.IsScheduledOn(KeyA));

        var listed = new PuloniaTaskTrigger { Instances = [KeyA, PuloniaInstanceKey.ChildSession] };
        Assert.False(listed.IsScheduledOn(PuloniaInstanceKey.Primary));
        Assert.True(listed.IsScheduledOn(KeyA));
        Assert.True(listed.IsScheduledOn(PuloniaInstanceKey.ChildSession));

        Assert.False(new PuloniaTaskTrigger { Kind = PuloniaTaskTriggerKind.Hotkey }.IsScheduledOn(PuloniaInstanceKey.Primary));
        // 副本保留执行实例，且与原配置互不影响
        var clone = listed.Clone();
        clone.Instances.Add(PuloniaInstanceKey.Primary);
        Assert.Equal([KeyA, PuloniaInstanceKey.ChildSession], listed.Instances);
    }

    /// <summary>保存计划时校验执行实例：热键不能选择执行实例，实例键必须有效且不重复。</summary>
    [Fact] public void ValidatorRejectsInvalidTriggerInstances()
    {
        ValidateWith(new PuloniaTaskTrigger { Instances = [KeyA, PuloniaInstanceKey.Primary] });
        ValidateWith(new PuloniaTaskTrigger { Kind = PuloniaTaskTriggerKind.Hotkey });

        Assert.Contains("热键触发器不能选择执行实例", Assert.Throws<PuloniaTaskValidationException>(() =>
            ValidateWith(new PuloniaTaskTrigger { Kind = PuloniaTaskTriggerKind.Hotkey, Instances = [PuloniaInstanceKey.Primary] })).Message);
        Assert.Contains("执行实例重复", Assert.Throws<PuloniaTaskValidationException>(() =>
            ValidateWith(new PuloniaTaskTrigger { Instances = [KeyA, KeyA] })).Message);
        Assert.Throws<PuloniaTaskValidationException>(() => ValidateWith(new PuloniaTaskTrigger { Instances = ["webview:A"] }));
        Assert.Throws<PuloniaTaskValidationException>(() => ValidateWith(new PuloniaTaskTrigger { Instances = ["other"] }));
    }

    /// <summary>
    /// 主实例与云原神 a 检查同一份计划：各自只提交自己负责的定时，游标写入各自的 state.json，运行记为在本实例执行；
    /// 执行实例去掉 a 后，a 不再处理该触发器并清理游标。
    /// </summary>
    [Fact] public async Task ScheduleRunsOnResponsibleInstanceAndPrunesCursor()
    {
        // 每个实例在自己的进程中有自己的存储对象，这里各建一个指向同一目录
        using var primaryStore = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        var clock = new TestClock();
        var plan = Plan();
        var primaryOnly = Trigger(clock);
        var onA = Trigger(clock);
        onA.Instances = [KeyA];
        plan.Triggers.AddRange([primaryOnly, onA]);
        plan = await primaryStore.SavePlanAsync(plan);
        await using var primary = Service(primaryStore, clock, PuloniaInstanceKey.Primary);
        await using var other = Service(otherStore, clock, KeyA);

        // 1. 两个实例都检查：各提交一次，触发器与执行实例对应
        await primary.CheckTriggersAsync([plan]);
        await other.CheckTriggersAsync([plan]);
        var primaryRun = await SingleRunAsync(primary, PuloniaInstanceKey.Primary);
        var otherRun = await SingleRunAsync(other, KeyA);
        Assert.Equal(primaryOnly.Id, primaryRun.TriggerId);
        Assert.Equal(onA.Id, otherRun.TriggerId);
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await primary.WaitForCompletionAsync(primaryRun.RunId).WaitAsync(WaitLimit)).Status);
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await other.WaitForCompletionAsync(otherRun.RunId).WaitAsync(WaitLimit)).Status);
        Assert.Equal(primaryOnly.Id, Assert.Single((await primaryStore.LoadStateAsync(PuloniaInstanceKey.Primary)).State.TriggerStates).TriggerId);
        Assert.Equal(onA.Id, Assert.Single((await otherStore.LoadStateAsync(KeyA)).State.TriggerStates).TriggerId);

        // 2. 再次检查同一发生时刻不重复提交
        await other.CheckTriggersAsync([plan]);
        await SingleRunAsync(other, KeyA);

        // 3. 执行实例改为只有主实例：a 清理游标，不再提交
        plan.Triggers.Single(trigger => trigger.Id == onA.Id).Instances = [PuloniaInstanceKey.Primary];
        await other.CheckTriggersAsync([plan]);
        Assert.Empty((await otherStore.LoadStateAsync(KeyA)).State.TriggerStates);
        await SingleRunAsync(other, KeyA);
    }

    /// <summary>实例目录名与实例键互相转换；无法识别或不合法的目录名返回 null。</summary>
    [Theory]
    [InlineData("primary", "primary")]
    [InlineData("child_session", "child_session")]
    [InlineData("webview-小号a", "webview:小号a")]
    [InlineData("webview-", null)]
    [InlineData("webview-ABC", null)]
    [InlineData("webview:abc", null)]
    [InlineData("other", null)]
    public void InstanceKeyFromDirectoryName(string directoryName, string? expected)
    {
        Assert.Equal(expected, PuloniaInstanceKey.TryFromDirectoryName(directoryName));
        if (expected is not null)
            Assert.Equal(directoryName, PuloniaInstanceKey.ToDirectoryName(expected));
    }

    /// <summary>仅清理本测试创建的独占目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    /// <summary>把触发器加入一份可运行的计划并按保存规则校验。</summary>
    /// <param name="trigger">待校验的触发器</param>
    private static void ValidateWith(PuloniaTaskTrigger trigger)
    {
        var plan = Plan();
        plan.Triggers.Add(trigger);
        PuloniaTaskValidator.ValidatePlan(plan);
    }

    /// <summary>本实例执行的唯一运行；只看本实例的运行，不受共享历史中其他实例运行的影响。</summary>
    /// <param name="service">任务服务</param>
    /// <param name="instanceKey">本实例的实例键</param>
    private static async Task<PuloniaTaskRunView> SingleRunAsync(PuloniaTaskService service, string instanceKey)
        => Assert.Single((await service.ListRunsAsync()).Where(run => run.Instance == instanceKey));

    /// <summary>建立不依赖游戏的可运行计划。</summary>
    private static PuloniaTaskPlan Plan() => new()
    {
        Name = "执行实例测试", RootTask = new PuloniaTask { Name = "根", Children = [new PuloniaTask
        { Name = "计算", TaskType = "csharp", Parameters = new JObject { ["operation"] = "sample.sum", ["values"] = new JArray(1, 2) } }] }
    };

    /// <summary>建立已启用且覆盖当前发生时刻的日程。</summary>
    /// <param name="clock">测试时钟</param>
    private static PuloniaTaskTrigger Trigger(TestClock clock) => new() { Enabled = true, ActivatedAtUtc = clock.GetUtcNow().AddHours(-1) };

    /// <summary>建立某个实例、仅使用受控 C# 执行器的调度服务。</summary>
    /// <param name="store">该实例的存储对象</param>
    /// <param name="clock">测试时钟</param>
    /// <param name="instanceKey">实例键</param>
    private static PuloniaTaskService Service(PuloniaTaskStore store, TestClock clock, string instanceKey)
        => new(store, new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())],
            new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance), clock,
            accountService: new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(instanceKey)));

    /// <summary>固定的 UTC 测试时钟，计时器仍使用真实有界等待。</summary>
    private sealed class TestClock : TimeProvider
    {
        /// <summary>测试时间戳：刚过 UTC+8 的 04:00，默认日程的发生时刻。</summary>
        private readonly long _ticks = DateTimeOffset.Parse("2026-10-04T04:00:02+08:00").UtcTicks;

        /// <summary>读取当前测试时间。</summary>
        public override DateTimeOffset GetUtcNow() => new(_ticks, TimeSpan.Zero);
    }
}
