using System.Reflection;
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
/// 整份计划复制、文件删除和保存并发回归，不启动应用或游戏。
/// </summary>
public sealed class PuloniaTaskPlanManagementTests : IDisposable
{
    /// <summary>
    /// 测试独占目录，避免接触程序真实计划和历史。
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pulonia-plans-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 整份复制重新映射账号预设和触发目标，保留参数类型、引用和配置，且不修改来源。
    /// </summary>
    [Fact]
    public void CopyPlan_RemapsInternalReferencesAndPreservesIndependentConfiguration()
    {
        var plan = CreatePlan("来源计划");
        var leaf = plan.RootTask.Children[0];
        leaf.IsEnabled = false;
        leaf.PresetId = "shared-preset";
        leaf.Policy.MaxRetries = 2;
        plan.Description = "计划说明";
        plan.RootTask.Children.Add(new PuloniaTask
        {
            Name = "引用计划", Source = new PuloniaTaskSource { Kind = "plan", PlanId = "other-plan" }
        });
        plan.Accounts.Add(new PuloniaTaskAccountBinding
        {
            AccountId = "account", PresetSelections = new Dictionary<string, string> { [leaf.Id] = "account-preset" }
        });
        plan.Triggers.Add(new PuloniaTaskTrigger
        {
            Enabled = true, TargetTaskId = leaf.Id, AccountId = "account", Kind = PuloniaTaskTriggerKind.Hotkey
        });
        plan.Triggers.Add(new PuloniaTaskTrigger { Enabled = true });
        plan.Revision = 7;
        var originalJson = PuloniaTaskJson.WritePlan(plan);

        var copy = PuloniaTaskJson.CopyPlan(plan);
        var copiedLeaf = copy.RootTask.Children[0];
        Assert.NotEqual(plan.Id, copy.Id);
        Assert.Equal(0, copy.Revision);
        Assert.NotEqual(plan.RootTask.Id, copy.RootTask.Id);
        for (var i = 0; i < plan.RootTask.Children.Count; i++)
            Assert.NotEqual(plan.RootTask.Children[i].Id, copy.RootTask.Children[i].Id);
        Assert.Equal(plan.Description, copy.Description);
        Assert.False(copiedLeaf.IsEnabled);
        Assert.Equal(leaf.PresetId, copiedLeaf.PresetId);
        Assert.Equal(2, copiedLeaf.Policy.MaxRetries);
        Assert.True(JToken.DeepEquals(leaf.Parameters, copiedLeaf.Parameters));
        Assert.Equal("other-plan", copy.RootTask.Children[1].Source!.PlanId);
        Assert.Equal("account-preset", Assert.Single(copy.Accounts).PresetSelections[copiedLeaf.Id]);
        Assert.Equal(copiedLeaf.Id, copy.Triggers[0].TargetTaskId);
        Assert.Null(copy.Triggers[1].TargetTaskId);
        Assert.All(copy.Triggers, trigger => Assert.False(trigger.Enabled));
        Assert.NotEqual(plan.Triggers[0].Id, copy.Triggers[0].Id);
        Assert.NotEqual(plan.Triggers[1].Id, copy.Triggers[1].Id);

        copiedLeaf.Parameters["values"] = new JArray(99);
        copiedLeaf.Policy.MaxRetries = 10;
        copy.RootTask.Children[1].Source!.PlanId = "changed-plan";
        Assert.Equal(originalJson, PuloniaTaskJson.WritePlan(plan));
    }

    /// <summary>
    /// 右键复制使用传入计划的当前草稿，名称不冲突，保存后副本紧邻来源且可重新加载。
    /// </summary>
    [Fact]
    public async Task CopyPlanCommand_UsesExplicitDraftAndPersistsUniqueCopyInOrder()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var clipboard = new PuloniaTaskClipboardService();
        var selected = new PuloniaTaskPlanDocumentViewModel(
            await store.SavePlanAsync(CreatePlan("当前选中")), [], clipboard, false);
        var source = new PuloniaTaskPlanDocumentViewModel(
            await store.SavePlanAsync(CreatePlan("右键目标")), [], clipboard, false);
        var page = CreatePage(store, service, clipboard);
        page.Documents.Add(selected);
        page.Documents.Add(source);
        page.Documents.Add(new PuloniaTaskPlanDocumentViewModel(CreatePlan("右键目标 - 副本"), [], clipboard, true));
        page.SelectedDocument = selected;
        source.ApplyMutation(() => source.Plan.RootTask.Children[0].Parameters["values"] = new JArray(42), source.SelectedNode);

        Assert.False(page.CopyPlanCommand.CanExecute(null));
        await page.CopyPlanCommand.ExecuteAsync(source);

        var copy = page.SelectedDocument!;
        Assert.Equal("右键目标 - 副本 2", copy.Name);
        Assert.Equal(2, page.Documents.IndexOf(copy));
        Assert.False(copy.IsDirty);
        var loaded = (await store.LoadPlanAsync(copy.Id))!;
        Assert.Equal(42, loaded.RootTask.Children[0].Parameters["values"]![0]!.Value<int>());
        Assert.Equal(copy.Name, loaded.RootTask.Name);
        Assert.Equal(1, (await store.LoadPlanAsync(source.Id))!.RootTask.Children[0].Parameters["values"]![0]!.Value<int>());
        Assert.Equal(page.Documents.Where(item => item.Revision > 0).Select(item => item.Id),
            (await store.ListPlansAsync()).Select(item => item.Id));
        page.IsBusy = true;
        Assert.False(page.CopyPlanCommand.CanExecute(source));
        Assert.False(page.DeletePlanCommand.CanExecute(source));
    }

    /// <summary>
    /// 删除清理正式文件与备份，并通知调度刷新；已有历史和运行状态原文保持不变。
    /// </summary>
    [Fact]
    public async Task DeletePlan_RemovesFilesAndPreservesHistoryAndState()
    {
        using var store = new PuloniaTaskStore(_directory);
        var plan = await store.SavePlanAsync(CreatePlan("待删除"));
        plan = await store.SavePlanAsync(plan);
        var record = new PuloniaTaskRunRecord
        {
            RequestId = Guid.NewGuid(), RunId = Guid.NewGuid(), Request = new PuloniaTaskRequest { PlanId = plan.Id },
            PlanName = plan.Name, SnapshotJson = "{}", Status = PuloniaTaskRunStatus.Succeeded,
            Message = "完成", SubmittedAt = DateTimeOffset.UtcNow
        };
        await store.ArchiveRunAsync(record);
        await store.SaveStateAsync(new PuloniaTaskState());
        var statePath = Path.Combine(_directory, "state.json");
        var stateJson = await File.ReadAllTextAsync(statePath);
        var path = Path.Combine(_directory, "plans", plan.Id + ".json");
        Assert.True(File.Exists(path + ".bak"));
        var version = store.PlanChangeVersion;

        await store.DeletePlanAsync(plan.Id, plan.Revision);

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".bak"));
        Assert.Null(await store.LoadPlanAsync(plan.Id));
        Assert.Empty(await store.ListPlansAsync());
        Assert.Equal(version + 1, store.PlanChangeVersion);
        Assert.Equal(record.RunId, Assert.Single(await store.ListHistoryAsync()).RunId);
        Assert.Equal(stateJson, await File.ReadAllTextAsync(statePath));
    }

    /// <summary>
    /// 即使引用节点禁用，删除也必须拒绝；先移除并保存引用后才允许删除。
    /// </summary>
    [Fact]
    public async Task DeletePlan_RejectsReferencesUntilTheyAreRemoved()
    {
        using var store = new PuloniaTaskStore(_directory);
        var target = await store.SavePlanAsync(CreatePlan("引用目标"));
        var source = CreatePlan("引用来源");
        source.RootTask.Children.Add(new PuloniaTask
        {
            Name = "禁用引用", IsEnabled = false, Source = new PuloniaTaskSource { Kind = "plan", PlanId = target.Id }
        });
        source = await store.SavePlanAsync(source);
        var version = store.PlanChangeVersion;

        var error = await Assert.ThrowsAsync<PuloniaTaskValidationException>(
            () => store.DeletePlanAsync(target.Id, target.Revision));
        Assert.Contains(source.Name, error.Message);
        Assert.NotNull(await store.LoadPlanAsync(target.Id));
        Assert.Equal(version, store.PlanChangeVersion);

        source.RootTask.Children.RemoveAt(1);
        await store.SavePlanAsync(source);
        await store.DeletePlanAsync(target.Id, target.Revision);
        Assert.Equal(source.Id, Assert.Single(await store.ListPlansAsync()).Id);
    }

    /// <summary>
    /// 其他存储实例已保存新修订时，不允许旧编辑会话删除更新后的计划。
    /// </summary>
    [Fact]
    public async Task DeletePlan_RejectsStaleRevision()
    {
        using var store = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        var plan = await store.SavePlanAsync(CreatePlan("旧修订"));
        var oldRevision = plan.Revision;
        plan.Name = "其他实例的新修订";
        plan = await otherStore.SavePlanAsync(plan);

        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => store.DeletePlanAsync(plan.Id, oldRevision));
        Assert.Equal(plan.Name, (await store.LoadPlanAsync(plan.Id))!.Name);
        Assert.True(File.Exists(Path.Combine(_directory, "plans", plan.Id + ".json.bak")));
    }

    /// <summary>
    /// 已在保存门前排队的保存遇到文档删除必须退出，不能重新创建文件或过早释放仍在使用的门。
    /// </summary>
    [Fact]
    public async Task PendingSave_DoesNotRecreateDeletedDocument()
    {
        using var store = new PuloniaTaskStore(_directory);
        await using var service = CreateService(store);
        var clipboard = new PuloniaTaskClipboardService();
        var document = new PuloniaTaskPlanDocumentViewModel(CreatePlan("已删除草稿"), [], clipboard, true);
        var page = CreatePage(store, service, clipboard);
        page.Documents.Add(document);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var acquire = typeof(PuloniaTaskPlanViewModel).GetMethod("GetSaveGate", flags)!;
        var release = typeof(PuloniaTaskPlanViewModel).GetMethod("ReleaseSaveGate", flags)!;
        var save = typeof(PuloniaTaskPlanViewModel).GetMethod("SaveDocumentAsync", flags)!;
        var gate = (SemaphoreSlim)acquire.Invoke(page, [document])!;
        await gate.WaitAsync();
        var pending = (Task<bool>)save.Invoke(page, [document])!;
        Assert.False(pending.IsCompleted);

        document.IsDeleted = true;
        page.Documents.Remove(document);
        release.Invoke(page, [document, gate]);

        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(await store.ListPlansAsync());
        Assert.False(await (Task<bool>)save.Invoke(page, [document])!);
        Assert.Throws<ObjectDisposedException>(() => gate.Wait(0));
    }

    /// <summary>
    /// 建立不启动游戏会话的纯任务服务。
    /// </summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store) => new(store,
        new PuloniaTaskBuilder(store), [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())],
        new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>
    /// 建立列表命令使用的页面模型，不创建 WPF 窗口。
    /// </summary>
    private static PuloniaTaskPlanViewModel CreatePage(PuloniaTaskStore store, PuloniaTaskService service,
        PuloniaTaskClipboardService clipboard) => new(store, clipboard, service, new PuloniaTaskResourceCatalog(),
        new PuloniaTaskHistoryViewModel(service, store));

    /// <summary>
    /// 建立包含数值、布尔和空值参数的简单计划。
    /// </summary>
    private static PuloniaTaskPlan CreatePlan(string name) => new()
    {
        Name = name,
        RootTask = new PuloniaTask
        {
            Name = name, Children =
            [new PuloniaTask
            {
                Name = "计算", TaskType = "csharp",
                Parameters = new JObject { ["operation"] = "sample.sum", ["values"] = new JArray(1, 2), ["flag"] = false, ["empty"] = null }
            }]
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
