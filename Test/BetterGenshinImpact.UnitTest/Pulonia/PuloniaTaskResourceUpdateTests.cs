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
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>只有根目录两个版本文件的内容变化才算更新，其他脚本及子目录同名文件均不参与。</summary>
    [Theory]
    [InlineData("records/账户.txt", false)]
    [InlineData("CDInfo/账户.json", false)]
    [InlineData("logs/run.log", false)]
    [InlineData("cache/result.json", false)]
    [InlineData("temp/output.txt", false)]
    [InlineData("main.js", true)]
    [InlineData("manifest.json", true)]
    [InlineData("README.md", false)]
    [InlineData("command.json", false)]
    [InlineData("helper.js", false)]
    [InlineData("assets/records/input.json", false)]
    [InlineData("assets/main.js", false)]
    [InlineData("assets/manifest.json", false)]
    [InlineData("CDInfo/helper.js", false)]
    [InlineData("records/module.mjs", false)]
    public async Task JavaScriptFingerprint_OnlyTracksManifestAndMain(string relativePath, bool changesVersion)
    {
        var root = Path.Combine(_directory, "script");
        await CreateJavaScriptProjectAsync(root);
        var file = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "原始内容");
        var original = await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(root);
        await File.WriteAllTextAsync(file, "更新内容");
        var changed = await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(root);
        Assert.Equal(changesVersion, original != changed);
        if (!changesVersion)
        {
            // 新增与删除无关文件也不能影响版本，不能只排除“内容修改”这一种变化。
            File.Delete(file);
            Assert.Equal(original, await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(root));
        }
    }

    /// <summary>检查、准备与续跑都只关心两个文件，无关资源变化或被占用不破坏快照。</summary>
    [Theory]
    [InlineData("CDInfo/账户.json", "main.js")]
    [InlineData("assets/helper.js", "manifest.json")]
    [InlineData("command.json", "main.js")]
    [InlineData("assets/main.js", "manifest.json")]
    [InlineData("helpers/manifest.json", "main.js")]
    public async Task JavaScriptOtherFiles_DoNotInvalidatePreparedOrResumedRun(string otherFile, string versionFile)
    {
        var root = Path.Combine(_directory, "script");
        await CreateJavaScriptProjectAsync(root);
        var runtimePath = Path.Combine(root, otherFile);
        Directory.CreateDirectory(Path.GetDirectoryName(runtimePath)!);
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
        using var exclusiveFile = new FileStream(runtimePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var clipboard = new PuloniaTaskClipboardService();
        var document = new PuloniaTaskPlanDocumentViewModel(plan, [], clipboard, isNew: false);
        var page = CreatePage(store, service, clipboard, document);
        await page.Session.RefreshResourceVersionsAsync();
        Assert.Equal(0, document.ResourceUpdateCount);
        Assert.Equal(0, document.ResourceErrorCount);
        var resumedId = await service.ResumeAsync(original.RunId, original.NodeResults.Single().TaskAddress);
        var resumed = await service.WaitForCompletionAsync(resumedId).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, resumed.Status);
        var freshId = await service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await service.WaitForCompletionAsync(freshId).WaitAsync(TimeSpan.FromSeconds(20))).Status);
        await File.WriteAllTextAsync(Path.Combine(root, versionFile), "更新的版本文件内容");
        await page.Session.RefreshResourceVersionsAsync();
        Assert.Equal(1, document.ResourceUpdateCount);
        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id }));
        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => service.ResumeAsync(original.RunId, original.NodeResults.Single().TaskAddress));
    }

    /// <summary>建立必需文件齐全的测试 JS 项目，不依赖任何真实脚本。</summary>
    private static async Task CreateJavaScriptProjectAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), "{\"name\":\"测试脚本\",\"version\":\"1.0\"}");
        await File.WriteAllTextAsync(Path.Combine(directory, "main.js"), "// 测试脚本");
    }

    /// <summary>建立无游戏依赖的任务服务，版本准备与执行历史仍走生产实现。</summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store, IPuloniaTaskExecutor executor)
        => new(store, new PuloniaTaskBuilder(store), [executor], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance));

    /// <summary>建立纯视图模型页面，测试不创建窗口也不启动页面轮询。</summary>
    private static PuloniaTaskPlanViewModel CreatePage(PuloniaTaskStore store, PuloniaTaskService service,
        PuloniaTaskClipboardService clipboard, PuloniaTaskPlanDocumentViewModel document)
    {
        var page = new PuloniaTaskPlanViewModel(
            new PuloniaPlanEditingSession(store, clipboard, service, new PuloniaTaskResourceCatalog()),
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
