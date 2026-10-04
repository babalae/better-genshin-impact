using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>资源更新标签、静态指纹与批量确认持久化回归，不运行游戏或真实 JS。</summary>
public sealed class PuloniaTaskResourceUpdateTests : IDisposable
{
    /// <summary>每个测试独占的临时目录，不触碰真实 User 数据。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pulonia-resources-" + Guid.NewGuid().ToString("N"));

    /// <summary>常见运行数据更新不改变 JS 静态版本，assets 内同名目录和可执行代码仍参与校验。</summary>
    [Theory]
    [InlineData("records/账户.txt", false)]
    [InlineData("CDInfo/账户.json", false)]
    [InlineData("logs/run.log", false)]
    [InlineData("cache/result.json", false)]
    [InlineData("temp/output.txt", false)]
    [InlineData("main.js", true)]
    [InlineData("assets/records/input.json", true)]
    [InlineData("CDInfo/helper.js", true)]
    [InlineData("records/module.mjs", true)]
    public async Task JavaScriptFingerprint_SeparatesRuntimeDataFromResources(string relativePath, bool changesVersion)
    {
        var root = Path.Combine(_directory, "script");
        var file = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "原始内容");
        var catalog = new PuloniaTaskResourceCatalog();
        var original = await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(root,
            await catalog.GetDirectoryFilesAsync(root, "*", true));
        await File.WriteAllTextAsync(file, "更新内容");
        var changed = await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(root,
            await catalog.GetDirectoryFilesAsync(root, "*", true));
        Assert.Equal(changesVersion, original != changed);
    }

    /// <summary>JS、路线、回放和目录更新分别标记，一次确认后落盘，旧执行历史保持不变。</summary>
    [Fact]
    public async Task BatchConfirmation_PersistsAllResourceKindsWithoutChangingHistory()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "script"));
        Directory.CreateDirectory(Path.Combine(_directory, "routes"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "script", "main.js"), "// 原脚本");
        await File.WriteAllTextAsync(Path.Combine(_directory, "route.json"), "{\"route\":1}");
        await File.WriteAllTextAsync(Path.Combine(_directory, "record.json"), "{\"record\":1}");
        await File.WriteAllTextAsync(Path.Combine(_directory, "routes", "a.json"), "{\"a\":1}");
        var plan = new PuloniaTaskPlan { Name = "多种资源", RootTask = new PuloniaTask { Name = "根", Children =
        [
            new PuloniaTask { Name = "脚本", TaskType = "javascript", Path = "script" },
            new PuloniaTask { Name = "路线", TaskType = "pathing", Path = "route.json" },
            new PuloniaTask { Name = "回放", TaskType = "keymouse", Path = "record.json", IsEnabled = false },
            new PuloniaTask { Name = "目录", Source = new PuloniaTaskSource { Kind = "directory", TaskType = "pathing",
                Path = Path.Combine(_directory, "routes") } }
        ] } };
        var executor = new ResourceExecutor(_directory);
        var versions = new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog());
        foreach (var task in plan.RootTask.Children)
        {
            var version = await versions.ReadCurrentVersionAsync(task, executor.Definitions);
            if (task.Source is not null) task.Source.Version = version;
            else task.ResourceVersion = version;
        }
        var oldVersions = plan.RootTask.Children.Select(task => task.Source?.Version ?? task.ResourceVersion).ToArray();
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        plan = await store.SavePlanAsync(plan);
        await using var service = CreateService(store, executor);
        var requestId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        var run = await service.WaitForCompletionAsync(requestId).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status);
        var historyJson = (await store.ListHistoryAsync()).Single().SnapshotJson;

        await File.WriteAllTextAsync(Path.Combine(_directory, "script", "main.js"), "// 新脚本");
        await File.WriteAllTextAsync(Path.Combine(_directory, "route.json"), "{\"route\":2}");
        await File.WriteAllTextAsync(Path.Combine(_directory, "record.json"), "{\"record\":2}");
        await File.WriteAllTextAsync(Path.Combine(_directory, "routes", "b.json"), "{\"b\":2}");
        var clipboard = new PuloniaTaskClipboardService();
        var document = new PuloniaTaskPlanDocumentViewModel(plan, [], clipboard, isNew: false);
        var page = CreatePage(store, service, clipboard, document);
        var unchangedPlanJson = PuloniaTaskJson.WritePlan(document.Plan);
        await page.RefreshResourceVersionsAsync();
        Assert.Equal(unchangedPlanJson, PuloniaTaskJson.WritePlan(document.Plan));
        Assert.Equal(4, document.ResourceUpdateCount);
        Assert.True(page.ConfirmResourceUpdatesCommand.CanExecute(document));
        Assert.All(document.RootNode.Children, node =>
        {
            Assert.True(node.HasResourceNotice);
            Assert.Equal("有更新", node.ResourceUpdateText);
        });

        // 页面使用同一批量模型操作；这里只绕过交互对话框，不绕过版本校验与存储。
        document.ApplyConfirmedResourceVersions(document.RootNode.Children.ToDictionary(node => node.Id, node => node.CurrentResourceVersion!));
        plan = await store.SavePlanAsync(document.Plan);
        await page.RefreshResourceVersionsAsync();
        Assert.Equal(0, document.ResourceUpdateCount);
        Assert.False(page.ConfirmResourceUpdatesCommand.CanExecute(document));
        Assert.All(document.RootNode.Children, node => Assert.False(node.HasResourceNotice));
        using var reopened = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        var restored = (await reopened.LoadPlanAsync(plan.Id))!;
        Assert.Equal(plan.RootTask.Children.Select(task => task.Source?.Version ?? task.ResourceVersion),
            restored.RootTask.Children.Select(task => task.Source?.Version ?? task.ResourceVersion));
        Assert.False(restored.RootTask.Children[2].IsEnabled);
        Assert.Equal(historyJson, (await reopened.ListHistoryAsync()).Single().SnapshotJson);
        Assert.Equal(run.SnapshotJson, (await service.GetRunAsync(requestId)).SnapshotJson);

        document.UndoCommand.Execute(null);
        Assert.Equal(oldVersions, document.Plan.RootTask.Children.Select(task => task.Source?.Version ?? task.ResourceVersion));
    }

    /// <summary>重复引用只统计一次，引用节点显示更新；确认目标计划后全部引用标签一起清除。</summary>
    [Fact]
    public async Task PlanReferences_AggregateUpdatesWithoutDuplicates()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "route.json"), "{}");
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        var executor = new ResourceExecutor(_directory);
        await using var service = CreateService(store, executor);
        var clipboard = new PuloniaTaskClipboardService();
        var target = new PuloniaTaskPlan { Name = "被引用计划", RootTask = new PuloniaTask { Name = "根", Children =
            [new PuloniaTask { Name = "待固定路线", TaskType = "pathing", Path = "route.json" }] } };
        var source = new PuloniaTaskPlan { Name = "来源计划", RootTask = new PuloniaTask { Name = "根", Children =
            [new PuloniaTask { Name = "引用一", Source = new PuloniaTaskSource { Kind = "plan", PlanId = target.Id } },
             new PuloniaTask { Name = "引用二", Source = new PuloniaTaskSource { Kind = "plan", PlanId = target.Id } }] } };
        var targetDocument = new PuloniaTaskPlanDocumentViewModel(target, [], clipboard, isNew: false);
        var sourceDocument = new PuloniaTaskPlanDocumentViewModel(source, [], clipboard, isNew: false);
        var page = CreatePage(store, service, clipboard, sourceDocument);
        page.Documents.Add(targetDocument);
        await page.RefreshResourceVersionsAsync();
        Assert.Equal(1, sourceDocument.ResourceUpdateCount);
        Assert.All(sourceDocument.RootNode.Children, node => Assert.True(node.HasResourceNotice));
        Assert.True(page.ConfirmResourceUpdatesCommand.CanExecute(sourceDocument));
        var resource = Assert.Single(targetDocument.RootNode.Children);
        targetDocument.ApplyConfirmedResourceVersions(new Dictionary<string, string> { [resource.Id] = resource.CurrentResourceVersion! });
        await page.RefreshResourceVersionsAsync();
        Assert.Equal(0, sourceDocument.ResourceUpdateCount);
        Assert.All(sourceDocument.RootNode.Children, node => Assert.False(node.HasResourceNotice));
    }

    /// <summary>资源缺失或旧检查结果不能写回固定版本，更不能部分更新同一文档。</summary>
    [Fact]
    public async Task MissingResource_RejectsBatchWithoutPartialMutation()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "route.json"), "{}");
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        await using var service = CreateService(store, new ResourceExecutor(_directory));
        var clipboard = new PuloniaTaskClipboardService();
        var document = new PuloniaTaskPlanDocumentViewModel(new PuloniaTaskPlan { Name = "缺失资源", RootTask = new PuloniaTask
        { Name = "根", Children = [new PuloniaTask { Name = "路线", TaskType = "pathing", Path = "route.json" },
            new PuloniaTask { Name = "缺失回放", TaskType = "keymouse", Path = "missing.json" }] } }, [], clipboard, isNew: false);
        var page = CreatePage(store, service, clipboard, document);
        await page.RefreshResourceVersionsAsync();
        Assert.Equal(1, document.ResourceUpdateCount);
        Assert.Equal(1, document.ResourceErrorCount);
        Assert.Equal("资源不可用", document.RootNode.Children[1].ResourceUpdateText);
        Assert.False(page.ConfirmResourceUpdatesCommand.CanExecute(document));
        var original = PuloniaTaskJson.WritePlan(document.Plan);
        Assert.Throws<InvalidOperationException>(() => document.ApplyConfirmedResourceVersions(
            document.RootNode.Children.ToDictionary(node => node.Id, node => node.CurrentResourceVersion ?? new string('a', 64))));
        Assert.Equal(original, PuloniaTaskJson.WritePlan(document.Plan));
        var available = document.RootNode.Children[0];
        Assert.Throws<InvalidOperationException>(() => document.ApplyConfirmedResourceVersions(
            new Dictionary<string, string> { [available.Id] = new string('b', 64) }));
        Assert.Equal(original, PuloniaTaskJson.WritePlan(document.Plan));
        available.Path = "new.json";
        Assert.Null(available.CurrentResourceVersion);
        Assert.False(available.HasResourceNotice);
    }

    /// <summary>检查与执行准备使用同一 JS 指纹范围，运行数据不会破坏快照，源码变化仍拒绝旧版本。</summary>
    [Fact]
    public async Task JavaScriptRuntimeWrites_DoNotInvalidatePreparedOrResumedRun()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "script", "CDInfo"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "script", "main.js"), "// 脚本");
        var runtimePath = Path.Combine(_directory, "script", "CDInfo", "账户.json");
        await File.WriteAllTextAsync(runtimePath, "{\"cd\":1}");
        var executor = new ResourceExecutor(_directory);
        var task = new PuloniaTask { Name = "脚本", TaskType = "javascript", Path = "script" };
        task.ResourceVersion = await new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog())
            .ReadCurrentVersionAsync(task, executor.Definitions);
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        var plan = await store.SavePlanAsync(new PuloniaTaskPlan { Name = "CD 数据", RootTask = new PuloniaTask { Name = "根", Children = [task] } });
        await using var service = CreateService(store, executor);
        var id = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        var original = await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(20));
        await File.WriteAllTextAsync(runtimePath, "{\"cd\":2}");
        var resumedId = await service.ResumeAsync(original.RunId, original.NodeResults.Single().TaskAddress);
        var resumed = await service.WaitForCompletionAsync(resumedId).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, resumed.Status);
        var freshId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await service.WaitForCompletionAsync(freshId).WaitAsync(TimeSpan.FromSeconds(20))).Status);
        await File.WriteAllTextAsync(Path.Combine(_directory, "script", "main.js"), "// 新脚本");
        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id }));
        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => service.ResumeAsync(original.RunId, original.NodeResults.Single().TaskAddress));
    }

    /// <summary>旧全目录指纹仅在完整目录未变化时兼容续跑，不能把旧记录静默升级为新范围。</summary>
    [Fact]
    public async Task LegacyJavaScriptSnapshot_OnlyResumesWhenFullDirectoryIsUnchanged()
    {
        var root = Path.Combine(_directory, "script");
        Directory.CreateDirectory(Path.Combine(root, "records"));
        await File.WriteAllTextAsync(Path.Combine(root, "main.js"), "// 原脚本");
        var runtime = Path.Combine(root, "records", "账户.txt");
        await File.WriteAllTextAsync(runtime, "原记录");
        var fullVersion = await PuloniaTaskResourceFingerprint.ComputeDirectoryVersionAsync(root,
            await new PuloniaTaskResourceCatalog().GetDirectoryFilesAsync(root, "*", true));
        var executor = new ResourceExecutor(_directory);
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        var plan = await store.SavePlanAsync(new PuloniaTaskPlan { Name = "旧指纹", RootTask = new PuloniaTask { Name = "根",
            Children = [new PuloniaTask { Name = "脚本", TaskType = "javascript", Path = "script" }] } });
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(plan,
            new PuloniaTaskBuildOptions { Definitions = executor.Definitions.ToList() });
        var json = JObject.Parse(snapshot.ToJson());
        json["root_task"]!["children"]![0]!["resource_version"] = fullVersion;
        plan.RootTask.Children[0].ResourceVersion = fullVersion;
        json["plan_json_by_id"]![plan.Id] = PuloniaTaskJson.WritePlan(plan);
        var record = new PuloniaTaskRunRecord { RequestId = Guid.NewGuid(), RunId = Guid.NewGuid(),
            Request = new PuloniaTaskRequest { PlanId = plan.Id, TimeoutSeconds = 600 }, PlanName = plan.Name,
            SnapshotJson = json.ToString(), Status = PuloniaTaskRunStatus.Cancelled,
            SubmittedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow, Message = "旧运行" };
        await store.ArchiveRunAsync(record);
        await using var service = CreateService(store, executor);
        var id = await service.ResumeAsync(record.RunId);
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(20))).Status);
        Assert.Equal(record.SnapshotJson, (await service.GetRunAsync(record.RequestId)).SnapshotJson);
        await File.WriteAllTextAsync(runtime, "新记录");
        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => service.ResumeAsync(record.RunId));
    }

    /// <summary>已禁用的资源有更新时仍展示标签，但卡片可运行其他已启用任务。</summary>
    [Fact]
    public async Task DisabledResourceUpdate_DoesNotBlockOtherTasks()
    {
        Directory.CreateDirectory(_directory);
        var route = Path.Combine(_directory, "route.json");
        var record = Path.Combine(_directory, "record.json");
        await File.WriteAllTextAsync(route, "{\"v\":1}");
        await File.WriteAllTextAsync(record, "{}");
        var plan = new PuloniaTaskPlan { Name = "禁用更新", RootTask = new PuloniaTask { Name = "根", Children =
            [new PuloniaTask { Name = "禁用路线", TaskType = "pathing", Path = "route.json", IsEnabled = false,
                ResourceVersion = await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(route) },
             new PuloniaTask { Name = "启用回放", TaskType = "keymouse", Path = "record.json",
                ResourceVersion = await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(record) }] } };
        await File.WriteAllTextAsync(route, "{\"v\":2}");
        using var store = new PuloniaTaskStore(Path.Combine(_directory, "store"));
        plan = await store.SavePlanAsync(plan);
        await using var service = CreateService(store, new ResourceExecutor(_directory));
        var clipboard = new PuloniaTaskClipboardService();
        var document = new PuloniaTaskPlanDocumentViewModel(plan, [], clipboard, isNew: false);
        var page = CreatePage(store, service, clipboard, document);
        await page.RunPlanCommand.ExecuteAsync(document);
        Assert.Equal(1, document.ResourceUpdateCount);
        Assert.True(document.RootNode.Children[0].HasResourceNotice);
        var run = await service.WaitForCompletionAsync(page.History.SelectedRun!.RequestId).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status);
        Assert.Contains(run.NodeResults, node => node.TaskType == "keymouse" && node.Status == PuloniaTaskNodeStatus.Succeeded);
    }

    /// <summary>建立无游戏依赖的任务服务，版本准备与执行历史仍走生产实现。</summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store, IPuloniaTaskExecutor executor)
        => new(store, new PuloniaTaskBuilder(store), [executor], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>建立纯视图模型页面，测试不创建窗口也不启动页面轮询。</summary>
    private static PuloniaTaskPlanViewModel CreatePage(PuloniaTaskStore store, PuloniaTaskService service,
        PuloniaTaskClipboardService clipboard, PuloniaTaskPlanDocumentViewModel document)
    {
        var page = new PuloniaTaskPlanViewModel(store, clipboard, service, new PuloniaTaskResourceCatalog(),
            new PuloniaTaskHistoryViewModel(service, store)) { SelectedDocument = document };
        page.Documents.Add(document);
        return page;
    }

    /// <summary>只清理本测试生成的独占临时目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>资源测试执行器，只返回成功，不加载真实脚本、路线或回放。</summary>
    private sealed class ResourceExecutor : IPuloniaTaskExecutor
    {
        /// <summary>与生产构建器一样声明资源类型及 JS 绑定标识。</summary>
        public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; }

        /// <summary>把全部测试资源约束到测试独占目录。</summary>
        public ResourceExecutor(string root) => Definitions =
        [
            new PuloniaTaskDefinition { TaskType = "javascript", ResourceId = "script", ResourceBaseDirectory = root },
            new PuloniaTaskDefinition { TaskType = "pathing", ResourceBaseDirectory = root },
            new PuloniaTaskDefinition { TaskType = "keymouse", ResourceBaseDirectory = root }
        ];

        /// <summary>不解释资源内容，资源读取与版本验证由真实准备和续跑服务负责。</summary>
        public Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task, PuloniaTaskExecutionContext context, CancellationToken ct)
            => Task.FromResult(PuloniaTaskOutcome.Success("测试资源已执行", new JObject()));
    }
}
