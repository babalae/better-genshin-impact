using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 添加任务菜单与“添加独立任务”命令的回归：菜单分段、排序和无参数任务的直接插入，不创建 WPF 窗口。
/// </summary>
public sealed class PuloniaSoloTaskMenuTests : IDisposable
{
    /// <summary>
    /// 当前测试独占的存储目录。
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pulonia-menu-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 独立任务子菜单按 Order 排序，Group 变化处插入分隔项，分隔项不携带任务类型。
    /// </summary>
    [Fact]
    public void BuildSoloTaskMenu_SortsByOrderAndSeparatesGroups()
    {
        var menu = PuloniaTaskMenuBuilder.BuildSoloTaskMenu(PuloniaSoloTestDefinitions.OneDragonCandidates());

        Assert.Equal(new[]
        {
            "领取邮件", "合成浓缩树脂", "领取每日奖励", "领取尘歌壶奖励", "-",
            "自动秘境", "自动首领讨伐", "自动幽境危战", "自动地脉花", "-",
            "返回主界面"
        }, menu.Select(item => item.IsSeparator ? "-" : item.Header));
        Assert.All(menu.Where(item => item.IsSeparator), item => Assert.Null(item.TaskType));
        Assert.All(menu.Where(item => !item.IsSeparator), item => Assert.True(PuloniaSoloTaskExecutor.IsSoloTaskType(item.TaskType)));
        Assert.All(menu, item => Assert.Empty(item.Children));
    }

    /// <summary>
    /// 完整菜单先放“独立任务”子菜单，再按固定顺序放资源类任务，未知类型使用显示名排在最后。
    /// </summary>
    [Fact]
    public void Build_PutsSoloSubmenuFirstThenKnownAndUnknownTypes()
    {
        PuloniaTaskDefinition[] others =
        [
            new() { TaskType = "csharp", DisplayName = "进程内 C#" },
            new() { TaskType = "custom.sample", DisplayName = "自定义示例" },
            new() { TaskType = "shell", DisplayName = "Shell" },
            new() { TaskType = "pathing", DisplayName = "地图追踪" },
            new() { TaskType = "keymouse", DisplayName = "录制回放" },
            new() { TaskType = "javascript", DisplayName = "JS 脚本" }
        ];

        var menu = PuloniaTaskMenuBuilder.Build([.. others, .. PuloniaSoloTestDefinitions.OneDragonCandidates()]);

        Assert.Equal(new[]
        {
            PuloniaTaskMenuBuilder.SoloTaskMenuHeader, "地图追踪任务…", "JS 脚本任务…", "录制回放任务…",
            "Shell 任务…", "进程内 C# 任务…", "自定义示例"
        }, menu.Select(item => item.Header));
        var solo = menu[0];
        Assert.Null(solo.TaskType);
        Assert.Equal(PuloniaTaskMenuBuilder.BuildSoloTaskMenu(PuloniaSoloTestDefinitions.OneDragonCandidates())
            .Select(item => item.IsSeparator ? "-" : item.TaskType), solo.Children.Select(item => item.IsSeparator ? "-" : item.TaskType));
        Assert.Equal(new[] { "pathing", "javascript", "keymouse", "shell", "csharp", "custom.sample" },
            menu.Skip(1).Select(item => item.TaskType));
    }

    /// <summary>
    /// 没有独立任务时不显示空的“独立任务”子菜单。
    /// </summary>
    [Fact]
    public void Build_WithoutSoloTasksOmitsSoloSubmenu()
    {
        var menu = PuloniaTaskMenuBuilder.Build([new PuloniaTaskDefinition { TaskType = "shell", DisplayName = "Shell" }]);

        var item = Assert.Single(menu);
        Assert.Equal("shell", item.TaskType);
    }

    /// <summary>
    /// 无参数的独立任务不弹创建对话框，直接以显示名插入到目标分组末尾，参数保持为空。
    /// </summary>
    [Fact]
    public async Task AddSoloTask_InsertsParameterlessTaskDirectly()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = new PuloniaTaskService(store, new PuloniaTaskBuilder(store),
            [new PuloniaDefinitionOnlyExecutor(PuloniaSoloTestDefinitions.OneDragonCandidates())],
            new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance));
        var page = new PuloniaTaskPlanViewModel(store, new PuloniaTaskClipboardService(), service,
            new PuloniaTaskResourceCatalog(), new PuloniaTaskHistoryViewModel(service, store));
        var document = new PuloniaTaskPlanDocumentViewModel(new PuloniaTaskPlan
        {
            Name = "菜单计划",
            RootTask = new PuloniaTask { Name = "菜单计划", TaskType = "group" }
        }, [], new PuloniaTaskClipboardService(), true);
        page.Documents.Add(document);
        var taskType = PuloniaSoloTaskExecutor.ToTaskType(ClaimMailRewardsTask.TaskId);
        Assert.Contains(page.SoloTaskMenu, item => item.TaskType == taskType);
        Assert.Equal(PuloniaTaskMenuBuilder.SoloTaskMenuHeader, page.AddTaskMenu[0].Header);

        page.AddSoloTaskCommand.Execute(new object[] { document.RootNode, taskType });

        var inserted = Assert.Single(document.RootNode.Children);
        Assert.Equal("领取邮件", inserted.Name);
        Assert.Equal(taskType, inserted.TaskType);
        Assert.Empty(inserted.Model.Parameters);
        Assert.Equal("独立任务", inserted.TypeDisplayName);
    }

    /// <summary>
    /// 参数不完整（菜单尚未完成绑定）时命令不做任何修改。
    /// </summary>
    [Fact]
    public async Task AddSoloTask_IgnoresIncompleteParameters()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = new PuloniaTaskService(store, new PuloniaTaskBuilder(store),
            [new PuloniaDefinitionOnlyExecutor(PuloniaSoloTestDefinitions.OneDragonCandidates())],
            new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance));
        var page = new PuloniaTaskPlanViewModel(store, new PuloniaTaskClipboardService(), service,
            new PuloniaTaskResourceCatalog(), new PuloniaTaskHistoryViewModel(service, store));
        var document = new PuloniaTaskPlanDocumentViewModel(new PuloniaTaskPlan
        {
            Name = "菜单计划",
            RootTask = new PuloniaTask { Name = "菜单计划", TaskType = "group" }
        }, [], new PuloniaTaskClipboardService(), true);
        page.Documents.Add(document);

        page.AddSoloTaskCommand.Execute(new object[] { document.RootNode });
        page.AddSoloTaskCommand.Execute((object[]?)null);

        Assert.Empty(document.RootNode.Children);
    }

    /// <summary>
    /// 只清理当前测试创建的唯一临时目录。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
