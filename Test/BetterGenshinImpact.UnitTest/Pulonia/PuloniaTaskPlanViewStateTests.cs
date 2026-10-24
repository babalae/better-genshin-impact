using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 任务树展开状态的持久化回归：展开状态存于 plan-view-state.json，计划与运行快照不包含该字段，
/// 展开或折叠不修改计划、不进入撤销。不启动应用或游戏。
/// </summary>
public sealed class PuloniaTaskPlanViewStateTests : IDisposable
{
    /// <summary>
    /// 测试独占目录，避免接触程序真实计划。
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>运行快照中的计划原文来自当前格式，不再携带展开状态。</summary>
    [Fact]
    public async Task Snapshot_DoesNotContainExpandedState()
    {
        using var store = new PuloniaTaskStore(_directory);
        var plan = await store.SavePlanAsync(CreatePlan());
        var executor = new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry());
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(plan,
            new PuloniaTaskBuildOptions { Definitions = executor.Definitions.ToList() });

        var json = JObject.Parse(snapshot.ToJson());
        foreach (var planJson in json["plan_json_by_id"]!.Children<JProperty>())
            Assert.Empty(JObject.Parse(planJson.Value.Value<string>()!).SelectTokens("$..children[*].is_expanded"));
    }

    /// <summary>
    /// 展开状态文件不存在时全部展开；保存后按新内容读回；已存在但损坏的文件明确报错。
    /// </summary>
    [Fact]
    public async Task PlanViewState_RoundTripsAndRejectsCorruption()
    {
        using var store = new PuloniaTaskStore(_directory);
        Assert.Empty((await store.LoadPlanViewStateAsync()).CollapsedTaskIds);
        var statePath = Path.Combine(_directory, "plan-view-state.json");
        Assert.False(File.Exists(statePath));

        var plan = await store.SavePlanAsync(CreatePlan());
        var groupId = plan.RootTask.Children[0].Id;
        await store.SavePlanViewStateAsync(Collapsed(plan.Id, groupId));
        Assert.Equal([groupId], (await store.LoadPlanViewStateAsync()).CollapsedTaskIds[plan.Id]);

        await File.WriteAllTextAsync(statePath, "{\"schema_version\":99,\"collapsed_task_ids\":{}}");
        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => store.LoadPlanViewStateAsync());
    }

    /// <summary>
    /// 编辑会话加载时恢复折叠状态；展开或折叠不标记计划已修改、不进入撤销，界面状态延迟写入独立文件。
    /// </summary>
    [Fact]
    public async Task ExpandState_IsRestoredAndSavedWithoutTouchingPlan()
    {
        using var store = new PuloniaTaskStore(_directory);
        var plan = await store.SavePlanAsync(CreatePlan());
        await store.SavePlanViewStateAsync(Collapsed(plan.Id, plan.RootTask.Children[0].Id));
        await using var service = new PuloniaTaskService(store, new PuloniaTaskBuilder(store),
            [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())],
            new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance));
        var session = new PuloniaPlanEditingSession(store, new PuloniaTaskClipboardService(), service, new PuloniaTaskResourceCatalog());

        await session.InitializeAsync();

        var document = Assert.Single(session.Documents);
        var group = document.RootNode.Children[0];
        Assert.False(group.IsExpanded);
        Assert.True(group.Children[0].IsExpanded);
        var planJson = PuloniaTaskJson.WritePlan(document.Plan);

        group.IsExpanded = true;

        Assert.True(group.IsExpanded);
        Assert.False(document.IsDirty);
        Assert.False(document.CanUndo);
        Assert.Equal(planJson, PuloniaTaskJson.WritePlan(document.Plan));
        // 防抖保存完成后，独立文件中不再记录该节点。
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((await store.LoadPlanViewStateAsync()).CollapsedTaskIds.ContainsKey(plan.Id) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.False((await store.LoadPlanViewStateAsync()).CollapsedTaskIds.ContainsKey(plan.Id));
    }

    /// <summary>撤销会重建树包装，展开状态属于编辑器，不随撤销回退。</summary>
    [Fact]
    public void Undo_KeepsExpandState()
    {
        var plan = CreatePlan();
        var group = plan.RootTask.Children[0];
        var document = new PuloniaTaskPlanDocumentViewModel(plan, [], new PuloniaTaskClipboardService(), false, [group.Id]);
        Assert.False(document.RootNode.Children[0].IsExpanded);

        document.RootNode.Children[0].Name = "改名后的分组";
        document.RootNode.Children[0].IsExpanded = true;
        document.UndoCommand.Execute(null);

        Assert.Equal("分组", document.RootNode.Children[0].Name);
        Assert.True(document.RootNode.Children[0].IsExpanded);
        Assert.Empty(document.GetCollapsedNodeIds());
    }

    /// <summary>
    /// 建立只折叠指定计划中一个节点的展开状态。
    /// </summary>
    private static PuloniaTaskPlanViewState Collapsed(string planId, string taskId)
    {
        var state = new PuloniaTaskPlanViewState();
        state.CollapsedTaskIds[planId] = [taskId];
        return state;
    }

    /// <summary>
    /// 建立“根 → 分组 → 叶子”三层的纯计算计划。
    /// </summary>
    private static PuloniaTaskPlan CreatePlan() => new()
    {
        Name = "展开状态",
        RootTask = new PuloniaTask
        {
            Name = "展开状态",
            Children =
            [
                new PuloniaTask
                {
                    Name = "分组",
                    Children = [new PuloniaTask { Name = "计算", TaskType = "csharp",
                        Parameters = new JObject { ["operation"] = "sample.sum", ["values"] = new JArray(1) } }]
                }
            ]
        }
    };

    /// <summary>
    /// 仅清理当前测试创建的唯一临时目录。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
