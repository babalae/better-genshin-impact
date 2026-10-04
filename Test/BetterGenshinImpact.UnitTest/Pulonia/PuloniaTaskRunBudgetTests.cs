using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>计划默认不限时及旧显式运行预算兼容回归，不访问游戏或 UI。</summary>
public sealed class PuloniaTaskRunBudgetTests : IDisposable
{
    /// <summary>每个测试独占的状态与历史目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pulonia-budget-" + Guid.NewGuid().ToString("N"));

    /// <summary>新请求、新触发器与缺省 JSON 都不限时；旧数字预算保持原语义。</summary>
    [Fact]
    public void DefaultsAndJson_KeepUnlimitedAndLegacyBudgetsDistinct()
    {
        Assert.Null(new PuloniaTaskRequest().TimeoutSeconds);
        Assert.Null(new PuloniaTaskTrigger().TimeoutSeconds);
        Assert.Null(PuloniaTaskJson.Read<PuloniaTaskRequest>("{\"plan_id\":\"test\"}").TimeoutSeconds);
        var plan = new PuloniaTaskPlan { Name = "预算兼容", RootTask = new PuloniaTask { Name = "根" } };
        var newEditor = new PuloniaTaskTriggerEditorViewModel(new PuloniaTaskTrigger(), plan);
        Assert.Null(newEditor.CreateTrigger().TimeoutSeconds);
        var legacyEditor = new PuloniaTaskTriggerEditorViewModel(new PuloniaTaskTrigger { TimeoutSeconds = 3600 }, plan);
        Assert.Equal(3600, legacyEditor.CreateTrigger().TimeoutSeconds);
        var oldRequest = PuloniaTaskJson.Read<PuloniaTaskRequest>("{\"plan_id\":\"test\",\"timeout_seconds\":600}");
        Assert.Equal(600, PuloniaTaskJson.Read<PuloniaTaskRequest>(PuloniaTaskJson.Write(oldRequest)).TimeoutSeconds);
        var unlimited = PuloniaTaskJson.Read<PuloniaTaskRequest>(PuloniaTaskJson.Write(new PuloniaTaskRequest { PlanId = "test" }));
        Assert.Null(unlimited.TimeoutSeconds);
    }

    /// <summary>默认不限时仍可主动停止，显式总预算仍能超时；两者都完成清理并归档最终状态。</summary>
    [Theory]
    [InlineData(null, PuloniaTaskRunStatus.Cancelled)]
    [InlineData(1d, PuloniaTaskRunStatus.TimedOut)]
    public async Task RunBudget_KeepsUserStopAndExplicitTimeout(double? budget, PuloniaTaskRunStatus expected)
    {
        using var store = new PuloniaTaskStore(_directory);
        var registry = new PuloniaCSharpTaskRegistry();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        registry.Register("test.wait", async (_, _, ct) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                return PuloniaTaskOutcome.Success("不应自然结束");
            }
            finally { cleaned = true; }
        });
        await using var service = CreateService(store, registry);
        var plan = await store.SavePlanAsync(new PuloniaTaskPlan { Name = "停止与超时", RootTask = new PuloniaTask
        { Name = "根", Children = [new PuloniaTask { Name = "等待", TaskType = "csharp", Parameters = new JObject { ["operation"] = "test.wait" } }] } });
        var id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id, TimeoutSeconds = budget });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        if (budget is null) await service.CancelAsync(id).WaitAsync(TimeSpan.FromSeconds(15));
        var run = await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(cleaned);
        Assert.Equal(expected, run.Status);
        var archived = Assert.Single(await store.ListHistoryAsync());
        Assert.Equal(expected, archived.Status);
        Assert.Equal(budget, archived.Request.TimeoutSeconds);
    }

    /// <summary>不限时只能用 null 表示，不能通过零、负数或无限数值绕过显式预算校验。</summary>
    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(604801d)]
    public async Task ExplicitInvalidBudgets_AreRejected(double budget)
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store, new PuloniaCSharpTaskRegistry());
        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => service.EnqueueAsync(
            new PuloniaTaskRequest { PlanId = "test", TimeoutSeconds = budget }));
        Assert.Throws<FormatException>(() => PuloniaTaskSchedule.Validate(new PuloniaTaskTrigger { TimeoutSeconds = budget }));
    }

    /// <summary>构建纯 C# 执行服务，不创建游戏会话。</summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store, PuloniaCSharpTaskRegistry registry)
        => new(store, new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(registry)], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>只清理本测试的独占临时目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
