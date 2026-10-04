using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 一条龙编辑器的默认清单、集合归属、根级插入和参数继承回归，不启动应用、游戏或 WPF 窗口。
/// </summary>
public sealed class PuloniaOneDragonEditorTests : IDisposable
{
    /// <summary>
    /// 当前测试独占的存储目录，避免接触程序真实的计划文件。
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pulonia-editor-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 默认清单按旧一条龙顺序包含八个内置任务，两份配置的计划与全部任务 ID 互相独立。
    /// </summary>
    [Fact]
    public void CreatePlan_KeepsDefaultOrderAndIndependentIds()
    {
        var first = PuloniaOneDragonDefaults.CreatePlan("周一一条龙");
        var second = PuloniaOneDragonDefaults.CreatePlan("周二一条龙");

        Assert.Equal(PuloniaTaskPlanPurpose.OneDragon, first.Purpose);
        Assert.Equal(PuloniaTaskPlanPurpose.OneDragon, second.Purpose);
        Assert.Equal(new[]
        {
            ("领取邮件", "builtin.claim_mail"),
            ("合成树脂", "builtin.craft_condensed_resin"),
            ("自动秘境", "builtin.auto_domain"),
            ("自动首领讨伐", "builtin.auto_boss"),
            ("自动幽境危战", "builtin.auto_stygian"),
            ("自动地脉花", "builtin.auto_ley_line"),
            ("领取每日奖励", "builtin.daily_rewards"),
            ("领取尘歌壶奖励", "builtin.serenitea_pot_rewards")
        }, first.RootTask.Children.Select(task => (task.Name, task.TaskType)).ToArray());
        Assert.NotEqual(first.Id, second.Id);

        // 两个根节点和全部叶子任务之间不得共享任何稳定 ID。
        var allIds = first.RootTask.Children.Concat(second.RootTask.Children)
            .Select(task => task.Id).Append(first.RootTask.Id).Append(second.RootTask.Id).ToArray();
        Assert.Equal(allIds.Length, allIds.Distinct().Count());
    }

    /// <summary>
    /// 初始化按用途把计划分派到普通与一条龙两个集合，底层存储列表仍同时包含两者。
    /// </summary>
    [Fact]
    public async Task InitializeCommand_RoutesPlansByPurposeAndKeepsBothInStore()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var general = await store.SavePlanAsync(new PuloniaTaskPlan
        {
            Name = "普通计划",
            RootTask = new PuloniaTask
            {
                Name = "普通计划",
                Children = [new PuloniaTask { Name = "计算", TaskType = "csharp", Parameters = new JObject { ["values"] = new JArray(1, 2) } }]
            }
        });
        var oneDragon = await store.SavePlanAsync(PuloniaOneDragonDefaults.CreatePlan("一条龙配置"));
        var page = CreatePage(store, service);

        await page.InitializeCommand.ExecuteAsync(null);

        var loadedGeneral = Assert.Single(page.Documents);
        Assert.Equal(general.Id, loadedGeneral.Id);
        Assert.Equal(PuloniaTaskPlanPurpose.General, loadedGeneral.Purpose);
        var loadedOneDragon = Assert.Single(page.OneDragonDocuments);
        Assert.Equal(oneDragon.Id, loadedOneDragon.Id);
        Assert.Equal(PuloniaTaskPlanPurpose.OneDragon, loadedOneDragon.Purpose);
        Assert.Equal(2, page.AllDocuments.Count());
        var listedIds = (await store.ListPlansAsync()).Select(plan => plan.Id).OrderBy(id => id, StringComparer.Ordinal);
        Assert.Equal(new[] { general.Id, oneDragon.Id }.OrderBy(id => id, StringComparer.Ordinal), listedIds);
    }

    /// <summary>首次加载期间另一个菜单进入时，必须等到文档加载完毕才能认为导航完成。</summary>
    [Fact]
    public async Task InitializeCommand_ConcurrentEntryWaitsForSameLoad()
    {
        using var store = new PuloniaTaskStore(_directory);
        await store.SavePlanAsync(new PuloniaTaskPlan { Name = "已有普通计划" });
        await using var service = CreateService(store);
        var page = CreatePage(store, service);
        // 暂时占用存储读写门，稳定复现导航在首次异步读盘时再次进入的时序。
        var storageGate = (SemaphoreSlim)typeof(PuloniaTaskStore)
            .GetField("_gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(store)!;
        await storageGate.WaitAsync();
        Task first;
        Task second;
        bool secondReturnedBeforeLoad;
        try
        {
            first = page.InitializeCommand.ExecuteAsync(null);
            second = page.InitializeCommand.ExecuteAsync(null);
            secondReturnedBeforeLoad = second.IsCompleted;
        }
        finally { storageGate.Release(); }
        await Task.WhenAll(first, second);

        Assert.False(secondReturnedBeforeLoad);
        Assert.True(page.IsInitialized);
        Assert.Single(page.Documents);
    }

    /// <summary>
    /// 选中根分组添加同类型重复任务仍插入根清单同级；移动后节点 ID 与参数保持独立。
    /// </summary>
    [Fact]
    public async Task InsertOneDragonTask_InsertsDuplicateAtRootLevelAndMoveKeepsIdentity()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var page = CreatePage(store, service);
        var document = new PuloniaTaskPlanDocumentViewModel(
            PuloniaOneDragonDefaults.CreatePlan("插入一条龙"), [], new PuloniaTaskClipboardService(), true);
        page.OneDragonDocuments.Add(document);
        var original = document.RootNode.Children[2];
        Assert.Equal("builtin.auto_domain", original.TaskType);

        page.InsertOneDragonTask(document, document.RootNode,
            new PuloniaTask { Name = "自动秘境", TaskType = "builtin.auto_domain" });

        var inserted = document.RootNode.Children[^1];
        Assert.Same(document.RootNode, inserted.Parent);
        Assert.Equal(9, document.RootNode.Children.Count);
        Assert.NotEqual(original.Id, inserted.Id);
        document.ApplyOneDragonParameters(inserted, new JObject { ["party_name"] = "副本专属队伍" });

        // 移动只改变树位置：包装、稳定 ID 和已提交的参数都跟随着同一个节点。
        document.MoveNode(inserted, document.RootNode, 0);

        Assert.Same(inserted, document.RootNode.Children[0]);
        Assert.Equal(9, document.RootNode.Children.Count);
        Assert.Equal("副本专属队伍", document.GetOneDragonParameters(inserted)["party_name"]!.Value<string>());
        var movedOriginal = document.RootNode.Children[3];
        Assert.Equal("自动秘境", movedOriginal.Name);
        Assert.Equal(original.Id, movedOriginal.Id);
        Assert.False(movedOriginal.Model.Parameters.ContainsKey("party_name"));
    }

    /// <summary>
    /// 关闭父组只暂停子项的生效状态，子节点本地开关保持原样。
    /// </summary>
    [Fact]
    public void GroupDisable_PreservesChildLocalSwitches()
    {
        var document = new PuloniaTaskPlanDocumentViewModel(
            PuloniaOneDragonDefaults.CreatePlan("开关一条龙"), [], new PuloniaTaskClipboardService(), true);
        var group = document.InsertNode(new PuloniaTask { Name = "周末加练", TaskType = "group" },
            document.RootNode, document.RootNode.Children.Count);
        var enabledChild = document.InsertNode(new PuloniaTask { Name = "首领讨伐", TaskType = "builtin.auto_boss" },
            group, 0);
        var disabledChild = document.InsertNode(new PuloniaTask { Name = "地脉花", TaskType = "builtin.auto_ley_line" },
            group, 1);
        disabledChild.IsEnabled = false;

        Assert.True(enabledChild.IsEffectivelyEnabled);
        Assert.False(disabledChild.IsEffectivelyEnabled);

        group.IsEnabled = false;

        Assert.False(group.Model.IsEnabled);
        Assert.True(enabledChild.Model.IsEnabled);
        Assert.False(disabledChild.Model.IsEnabled);
        Assert.False(enabledChild.IsEffectivelyEnabled);
        Assert.True(enabledChild.IsDisabledByParent);
        Assert.False(disabledChild.IsEffectivelyEnabled);
        Assert.False(disabledChild.IsDisabledByParent);

        group.IsEnabled = true;

        Assert.True(enabledChild.IsEffectivelyEnabled);
        Assert.False(disabledChild.IsEffectivelyEnabled);
    }

    /// <summary>
    /// 卡片参数提交只把真实变化写入节点覆盖，默认值与预设继承不落盘，重复节点互不影响。
    /// </summary>
    [Fact]
    public void ApplyOneDragonParameters_PersistsOnlyChangedFieldsAndIsolatesDuplicates()
    {
        var definition = new PuloniaCombatTaskExecutor().Definitions
            .First(item => item.TaskType == "builtin.auto_boss");
        var preset = new PuloniaTaskPreset
        {
            Name = "首领预设", TaskType = "builtin.auto_boss", SchemaVersion = definition.SchemaVersion,
            Values = new JObject { ["run_count"] = 2 }
        };
        var document = new PuloniaTaskPlanDocumentViewModel(new PuloniaTaskPlan
        {
            Name = "参数一条龙",
            RootTask = new PuloniaTask
            {
                Name = "参数一条龙",
                Children =
                [
                    new PuloniaTask { Name = "首领讨伐A", TaskType = "builtin.auto_boss", PresetId = preset.Id },
                    new PuloniaTask { Name = "首领讨伐B", TaskType = "builtin.auto_boss" }
                ]
            }
        }, [preset], new PuloniaTaskClipboardService(), false);
        document.SetDefinitions(new PuloniaCombatTaskExecutor().Definitions);
        var nodeA = document.RootNode.Children[0];
        var nodeB = document.RootNode.Children[1];

        // 打开配置不产生草稿：预设次数只是继承结果，节点自身没有持久化任何参数。
        Assert.Equal(2, document.GetOneDragonParameters(nodeA)["run_count"]!.Value<int>());
        Assert.Empty(nodeA.Model.Parameters);
        Assert.Empty(nodeB.Model.Parameters);

        document.ApplyOneDragonParameters(nodeA, new JObject { ["team_name"] = "固定队伍" });

        Assert.Single(nodeA.Model.Parameters);
        Assert.Equal("固定队伍", nodeA.Model.Parameters["team_name"]!.Value<string>());
        Assert.False(nodeA.Model.Parameters.ContainsKey("run_count"));
        Assert.Equal(2, document.GetOneDragonParameters(nodeA)["run_count"]!.Value<int>());
        Assert.NotEqual("固定队伍", document.GetOneDragonParameters(nodeB)["team_name"]?.Value<string>());

        document.ApplyOneDragonParameters(nodeB, new JObject { ["run_count"] = 5 });

        Assert.Single(nodeB.Model.Parameters);
        Assert.Equal(5, nodeB.Model.Parameters["run_count"]!.Value<int>());
        Assert.Single(nodeA.Model.Parameters);
        Assert.Equal("固定队伍", nodeA.Model.Parameters["team_name"]!.Value<string>());
        Assert.Equal(2, document.GetOneDragonParameters(nodeA)["run_count"]!.Value<int>());
        Assert.NotEqual("固定队伍", document.GetOneDragonParameters(nodeB)["team_name"]?.Value<string>());
    }

    /// <summary>不同计划允许复用节点 ID，切换配置不能串用未提交的参数草稿。</summary>
    [Fact]
    public async Task SettingsDraft_SameNodeIdInDifferentPlansRemainsIndependent()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var editor = CreatePage(store, service);
        var page = new PuloniaOneDragonViewModel(editor, service, new PuloniaTaskResourceCatalog(), store);
        var first = CreateConfiguration(service.Definitions, "配置一", "same-node-id");
        var second = CreateConfiguration(service.Definitions, "配置二", "same-node-id");
        editor.OneDragonDocuments.Add(first);
        editor.OneDragonDocuments.Add(second);

        page.SelectedConfiguration = first;
        var firstDraft = page.SettingsFields.Single(field => field.Name == "values");
        firstDraft.TextValue = "[4,5]";
        page.SelectedConfiguration = second;
        var secondDraft = page.SettingsFields.Single(field => field.Name == "values");
        Assert.NotSame(firstDraft, secondDraft);
        Assert.Equal("[1,2,3]", secondDraft.BuildValue()!.ToString(Newtonsoft.Json.Formatting.None));
        secondDraft.TextValue = "[9]";
        page.ApplySettingsCommand.Execute(null);

        page.SelectedConfiguration = first;
        Assert.Same(firstDraft, page.SettingsFields.Single(field => field.Name == "values"));
        Assert.Equal("[4,5]", firstDraft.TextValue);
        Assert.Empty(first.RootNode.Children[0].Model.Parameters);
        Assert.Equal("[9]", second.GetOneDragonParameters(second.RootNode.Children[0])["values"]!.ToString(Newtonsoft.Json.Formatting.None));
    }

    /// <summary>撤销已提交编辑不应先提交设置草稿，无效 JSON 也不能阻止撤销。</summary>
    [Theory]
    [InlineData("[8]")]
    [InlineData("[")]
    public async Task UndoConfiguration_DoesNotCommitOrValidatePendingDraft(string draft)
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var page = new PuloniaOneDragonViewModel(CreatePage(store, service), service, new PuloniaTaskResourceCatalog(), store);
        var document = CreateConfiguration(service.Definitions, "配置一", "node-one");
        page.SelectedConfiguration = document;
        page.SettingsFields.Single(field => field.Name == "values").TextValue = draft;
        document.RootNode.Children[0].Name = "修改后的名称";

        page.UndoConfigurationCommand.Execute(null);

        Assert.Equal("原始名称", document.RootNode.Children[0].Name);
        Assert.Empty(document.RootNode.Children[0].Model.Parameters);
        Assert.False(document.IsDirty);
        Assert.Same(document.RootNode.Children[0], page.SelectedTask);
    }

    /// <summary>撤销重建一份配置的节点时，只重置该配置草稿，其他配置的输入必须保留。</summary>
    [Fact]
    public async Task UndoConfiguration_PreservesDraftsInOtherPlans()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var page = new PuloniaOneDragonViewModel(CreatePage(store, service), service, new PuloniaTaskResourceCatalog(), store);
        var first = CreateConfiguration(service.Definitions, "配置一", "node-one");
        var second = CreateConfiguration(service.Definitions, "配置二", "node-two");
        page.SelectedConfiguration = second;
        var secondDraft = page.SettingsFields.Single(field => field.Name == "values");
        secondDraft.TextValue = "[7]";
        page.SelectedConfiguration = first;
        first.RootNode.Children[0].Name = "修改后的名称";
        page.UndoConfigurationCommand.Execute(null);

        page.SelectedConfiguration = second;
        Assert.Same(secondDraft, page.SettingsFields.Single(field => field.Name == "values"));
        Assert.Equal("[7]", secondDraft.TextValue);
        Assert.Empty(second.RootNode.Children[0].Model.Parameters);
    }

    /// <summary>关闭任务的无效输入保留为草稿，不能阻止同一配置中其他启用任务运行。</summary>
    [Fact]
    public async Task RunCommand_DisabledInvalidDraftDoesNotBlockEnabledTask()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var editor = CreatePage(store, service);
        var page = new PuloniaOneDragonViewModel(editor, service, new PuloniaTaskResourceCatalog(), store);
        var document = CreateConfiguration(service.Definitions, "配置一", "disabled-node");
        document.InsertNode(new PuloniaTask { Name = "启用的计算任务", TaskType = "csharp" }, document.RootNode, 1);
        editor.OneDragonDocuments.Add(document);
        page.SelectedConfiguration = document;
        var disabledNode = document.RootNode.Children[0];
        page.SelectedTask = disabledNode;
        var draft = page.SettingsFields.Single(field => field.Name == "values");
        draft.TextValue = "[";
        disabledNode.IsEnabled = false;

        await page.RunCommand.ExecuteAsync(null);

        var request = Assert.Single(await service.ListRunsAsync());
        var completed = await service.WaitForCompletionAsync(request.RequestId);
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, completed.Status);
        Assert.Contains(completed.NodeResults, item => item.TaskAddress.EndsWith("/" + disabledNode.Id, StringComparison.Ordinal)
            && item.Status == PuloniaTaskNodeStatus.Skipped);
        Assert.Contains(completed.NodeResults, item => item.TaskName == "启用的计算任务" && item.Status == PuloniaTaskNodeStatus.Succeeded);
        Assert.Same(draft, page.SettingsFields.Single(field => field.Name == "values"));
        Assert.Equal("[", draft.TextValue);
        Assert.Empty(disabledNode.Model.Parameters);
    }

    /// <summary>建立具有已保存检查点和真实能力定义的纯计算一条龙文档。</summary>
    private static PuloniaTaskPlanDocumentViewModel CreateConfiguration(
        IReadOnlyList<PuloniaTaskDefinition> definitions, string name, string nodeId)
    {
        var document = new PuloniaTaskPlanDocumentViewModel(new PuloniaTaskPlan
        {
            Name = name, Purpose = PuloniaTaskPlanPurpose.OneDragon,
            RootTask = new PuloniaTask { Children = [new PuloniaTask { Id = nodeId, Name = "原始名称", TaskType = "csharp" }] }
        }, [], new PuloniaTaskClipboardService(), false);
        document.SetDefinitions(definitions);
        return document;
    }

    /// <summary>
    /// 建立不启动游戏会话的纯任务服务。
    /// </summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store) => new(store,
        new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())],
        new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>
    /// 建立一条龙插入命令使用的页面模型，不创建 WPF 窗口。
    /// </summary>
    private static PuloniaTaskPlanViewModel CreatePage(PuloniaTaskStore store, PuloniaTaskService service)
        => new(store, new PuloniaTaskClipboardService(), service, new PuloniaTaskResourceCatalog(),
            new PuloniaTaskHistoryViewModel(service, store));

    /// <summary>
    /// 只清理当前测试创建的唯一临时目录。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
