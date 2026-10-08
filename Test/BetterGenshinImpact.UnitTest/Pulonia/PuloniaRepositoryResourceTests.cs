using System.Text;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using LibGit2Sharp;
using Newtonsoft.Json.Linq;
using System.IO.Compression;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>通过真实临时 Git 对象库和离线文件仓库验证资源隔离，不启动游戏。</summary>
public sealed class PuloniaRepositoryResourceTests : IDisposable
{
    /// <summary>每个测试独立的临时根目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgi-repository-tests-" + Guid.NewGuid().ToString("N"));
    /// <summary>测试文件严格使用 UTF-8 无 BOM。</summary>
    private static readonly UTF8Encoding Utf8 = new(false);
    /// <summary>生成符合旧脚本协议的项目清单。</summary>
    private const string ScriptManifest = "{\"manifest_version\":1,\"name\":\"测试脚本\",\"version\":\"1\",\"main\":\"main.js\",\"saved_files\":[\"progress.json\"]}";

    /// <summary>建立隔离的仓库管理器。</summary>
    private ScriptRepositoryStore CreateStore() => new(Path.Combine(_root, "resources"), Path.Combine(_root, "repos"));

    /// <summary>建立小型来源仓库，包含 JS、公共包及由旧宿主 API 读取的路线。</summary>
    private string CreateSource(string name, bool git = true)
    {
        var directory = Path.Combine(_root, "repos", name);
        // JS 项目在官方索引中是带版本的目录节点，正文不列在 children 中。
        Write(directory, "repo.json", new JObject
        {
            ["indexes"] = new JArray
            {
                new JObject { ["name"] = "js", ["type"] = "directory", ["children"] = new JArray
                { new JObject { ["name"] = "示例", ["type"] = "directory", ["version"] = "1" } } },
                new JObject { ["name"] = "pathing", ["type"] = "directory", ["children"] = new JArray
                {
                    new JObject { ["name"] = "路线.json", ["type"] = "file" },
                    new JObject { ["name"] = "子目录", ["type"] = "directory", ["children"] = new JArray
                    { new JObject { ["name"] = "第二条.json", ["type"] = "file" } } }
                } }
            }
        }.ToString());
        Write(directory, "repo/js/示例/main.js", "// 初始版本");
        Write(directory, "repo/js/示例/manifest.json", ScriptManifest);
        Write(directory, "repo/js/示例/helper.js", "// 旧模块");
        Write(directory, "repo/pathing/路线.json", "{\"route\":1}");
        Write(directory, "repo/pathing/子目录/第二条.json", "{\"route\":2}");
        Write(directory, "packages/shared.js", "// 旧公共包");
        if (git) { Repository.Init(directory); Commit(directory); }
        return directory;
    }

    /// <summary>在临时来源内写入一个普通文件。</summary>
    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Utf8);
    }

    /// <summary>提交测试变化，以真实提交身份作为来源版本。</summary>
    private static void Commit(string directory)
    {
        using var repository = new Repository(directory);
        Commands.Stage(repository, "*");
        var author = new Signature("resource-test", "resource-test@example.invalid", DateTimeOffset.UtcNow);
        repository.Commit("resource version", author, author);
    }

    /// <summary>固定当前来源并建立一个明确的资源引用。</summary>
    private static async Task<ScriptResourceReference> ReferenceAsync(ScriptRepositoryStore store, string source,
        string relative = "js/示例")
    {
        var registration = await store.RegisterAsync(source);
        using var snapshot = await store.OpenCurrentAsync(registration.Id);
        return new ScriptResourceReference
        {
            RepositoryId = registration.Id, RelativePath = relative, ApprovedRevision = snapshot.Revision
        };
    }

    /// <summary>ZIP 导入前必须识别唯一仓库根，并拒绝缺少 indexes 清单的 repo.json。</summary>
    [Fact]
    public async Task RepositoryZipValidation_RequiresValidRepoJson()
    {
        var store = CreateStore();
        var source = CreateSource("zip-valid", false);
        var validZip = Path.Combine(_root, "valid.zip");
        ZipFile.CreateFromDirectory(source, validZip);
        await store.ValidateRepositoryZipAsync(validZip);

        var invalidSource = CreateSource("zip-invalid", false);
        Write(invalidSource, "repo.json", "{}");
        var invalidZip = Path.Combine(_root, "invalid.zip");
        ZipFile.CreateFromDirectory(invalidSource, invalidZip);
        var error = await Assert.ThrowsAsync<IOException>(() => store.ValidateRepositoryZipAsync(invalidZip));
        Assert.Contains("indexes", error.Message);
    }

    /// <summary>只有两个版本文件触发更新；其他来源文件改变后旧缓存仍保留原内容。</summary>
    [Theory]
    [InlineData("repo/js/示例/main.js", true)]
    [InlineData("repo/js/示例/manifest.json", true)]
    [InlineData("repo/js/示例/helper.js", false)]
    [InlineData("packages/shared.js", false)]
    [InlineData("repo/pathing/路线.json", false)]
    public async Task GitUpdates_OnlyVersionFilesNotify_AndDoNotReplaceApprovedContent(string file, bool notify)
    {
        var store = CreateStore();
        var source = CreateSource("official");
        var reference = await ReferenceAsync(store, source);
        var cache = await store.MaterializeAsync(reference, "javascript");
        var originalVersion = await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(cache);
        var task = new PuloniaTask { TaskType = "javascript", Resource = reference, ResourceVersion = originalVersion };
        var service = new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog(store));
        Assert.Equal(originalVersion, await service.ReadCurrentVersionAsync(task, []));

        Write(source, file, file.EndsWith("manifest.json") ? ScriptManifest.Replace("\"1\"", "\"2\"") : "// 新内容");
        Commit(source);
        var current = await service.ReadCurrentStateAsync(task, []);
        Assert.Equal(notify, originalVersion != current.CurrentContentVersion);
        if (file.EndsWith("manifest.json"))
        {
            Assert.Equal("1", current.ApprovedDeclaredVersion);
            Assert.Equal("2", current.CurrentDeclaredVersion);
        }
        Assert.Equal(cache, await store.MaterializeAsync(reference, "javascript"));
        Assert.Equal("// 旧模块", File.ReadAllText(Path.Combine(cache, "helper.js")));
        Assert.Equal("// 旧公共包", File.ReadAllText(Path.Combine(cache, "packages/shared.js")));
    }

    /// <summary>重新克隆或移除当前来源不破坏旧版本；旧宿主 API 从确认时版本查询路线。</summary>
    [Fact]
    public async Task ApprovedSnapshot_SurvivesSourceDeletion_AndRestoresMissingCache()
    {
        var store = CreateStore();
        var source = CreateSource("official");
        var reference = await ReferenceAsync(store, source);
        var cache = await store.MaterializeAsync(reference, "javascript");
        DeleteTemporary(source);
        DeleteTemporary(Path.GetDirectoryName(cache)!);
        var restored = await store.MaterializeAsync(reference, "javascript");
        Assert.Equal("// 初始版本", File.ReadAllText(Path.Combine(restored, "main.js")));
        using var context = new ScriptRepositoryResourceContext(await store.OpenApprovedAsync(reference));
        var host = new AutoPathingScript(restored, null, CancellationToken.None, context);
        Assert.Equal("{\"route\":1}", host.ReadTextSync("路线.json"));
        Assert.True(host.IsFile("路线.json"));
        Assert.True(host.IsFolder("子目录"));
        Assert.Contains("子目录", host.ReadPathSync());
        Assert.Throws<FileNotFoundException>(() => host.ReadTextSync("缺失.json"));
    }

    /// <summary>同名资源按仓库分组，索引与计划引用不会混合来源。</summary>
    [Fact]
    public async Task Catalog_SeparatesSameNamedSources_AndDoesNotUseSubscriptionDirectories()
    {
        var store = CreateStore();
        CreateSource("official");
        var thirdParty = CreateSource("third-party");
        Write(thirdParty, "repo/pathing/路线.json", "{\"third_party\":true}");
        Commit(thirdParty);
        var catalog = new PuloniaTaskResourceCatalog(store);
        var scripts = await catalog.GetIndexAsync(new PuloniaTaskDefinition { TaskType = "javascript" });
        Assert.Equal(2, scripts.Resources.Count);
        Assert.Equal(2, scripts.Resources.Select(r => r.Resource!.ScopeKey).Distinct().Count());
        var routes = await catalog.GetIndexAsync(new PuloniaTaskDefinition { TaskType = "pathing" });
        Assert.Equal(2, routes.Resources.Count(r => r.IsRootDirectory));
        var sameNames = routes.Resources.Where(r => r.DisplayName == "路线").ToArray();
        Assert.Equal(2, sameNames.Length);
        Assert.NotEqual(await store.MaterializeAsync(sameNames[0].Resource!, "pathing"),
            await store.MaterializeAsync(sameNames[1].Resource!, "pathing"));
    }

    /// <summary>确认采用整份新项目，工作目录隔离写入并按脚本共享进度。</summary>
    [Fact]
    public async Task ConfirmedUpdate_PreservesSharedSavedFiles_AndWorkspaceLockCancels()
    {
        var store = CreateStore();
        var source = CreateSource("official");
        var reference = await ReferenceAsync(store, source);
        using (var workspace = await store.AcquireWorkspaceAsync(reference))
        {
            File.WriteAllText(Path.Combine(workspace.Path, "progress.json"), "{\"done\":true}");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.AcquireWorkspaceAsync(reference, cancellation.Token));
        }
        Write(source, "repo/js/示例/main.js", "// 更新入口");
        Write(source, "repo/js/示例/helper.js", "// 更新模块");
        Commit(source);
        var service = new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog(store));
        var task = new PuloniaTask { TaskType = "javascript", Resource = reference };
        var version = await service.ReadCurrentVersionAsync(task, []);
        var adopted = await service.PrepareUpdateAsync(task, version!);
        Assert.Same(reference, task.Resource);
        using var updatedWorkspace = await store.AcquireWorkspaceAsync(adopted!);
        Assert.Equal("{\"done\":true}", File.ReadAllText(Path.Combine(updatedWorkspace.Path, "progress.json")));
        Assert.Equal("// 更新模块", File.ReadAllText(Path.Combine(updatedWorkspace.Path, "helper.js")));
        Assert.Equal("// 旧模块", File.ReadAllText(Path.Combine(await store.MaterializeAsync(reference, "javascript"), "helper.js")));
    }

    /// <summary>目录引用在准备及快照恢复时保持旧清单，来源更新不改变本次执行树。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DirectoryReference_FreezesJsonList_AndRoundTripsSnapshot(bool git)
    {
        var store = CreateStore();
        var source = CreateSource("official", git);
        var reference = await ReferenceAsync(store, source, "pathing");
        await store.MaterializeAsync(reference, "pathing");
        using var approved = await store.OpenApprovedAsync(reference);
        var version = ScriptRepositoryStore.ComputeVersion(approved, "pathing", "pathing", directory: true);
        var group = new PuloniaTask
        {
            TaskType = "group", Source = new PuloniaTaskSource { Kind = "directory", Resource = reference, TaskType = "pathing", Version = version }
        };
        var plan = new PuloniaTaskPlan();
        plan.RootTask.Children.Add(group);
        var builder = new PuloniaTaskBuilder(new PuloniaTaskStore(Path.Combine(_root, "plans")), store);
        var options = new PuloniaTaskBuildOptions { Definitions = [new() { TaskType = "pathing" }] };
        var first = await builder.BuildAsync(plan, options);
        Write(source, "repo/pathing/新增.json", "{}");
        if (git) Commit(source); else store.NotifyUpdated(source);
        var second = await builder.BuildAsync(plan, options);
        Assert.Equal(2, second.RootTask.Children.Single().Children.Count);
        Assert.Equal(first.RootTask.Children.Single().ResourceVersion, second.RootTask.Children.Single().ResourceVersion);
        var restored = PuloniaTaskJson.ReadSnapshot(second.ToJson());
        Assert.All(restored.RootTask.Children.Single().Children, child => Assert.Equal(reference.ApprovedRevision, child.Resource!.ApprovedRevision));
    }

    /// <summary>版本不匹配或提取失败不能改变任务已经批准的引用。</summary>
    [Fact]
    public async Task FailedPreparation_DoesNotModifyPlanReference()
    {
        var store = CreateStore();
        var source = CreateSource("official");
        var reference = await ReferenceAsync(store, source);
        var task = new PuloniaTask { TaskType = "javascript", Resource = reference };
        var service = new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog(store));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareUpdateAsync(task, new string('0', 64)));
        Assert.Same(reference, task.Resource);
        Assert.Throws<ArgumentException>(() => reference.WithPath("../outside.json"));
    }

    /// <summary>实际拉取只检出索引时仍直接浏览 Git 树，不要求磁盘上存在 repo/ 正文。</summary>
    [Fact]
    public async Task SparseCheckout_BrowsesAndExtractsGitObjects()
    {
        var store = CreateStore();
        var source = CreateSource("sparse");
        DeleteTemporary(Path.Combine(source, "repo"));
        var catalog = new PuloniaTaskResourceCatalog(store);
        var index = await catalog.GetIndexAsync(new PuloniaTaskDefinition { TaskType = "javascript" });
        var resource = Assert.Single(index.Resources).Resource!;
        var cache = await store.MaterializeAsync(resource, "javascript");
        Assert.Equal("// 初始版本", File.ReadAllText(Path.Combine(cache, "main.js")));
        Assert.True(index.Resources.Single().LastWriteTime > DateTime.UnixEpoch);
    }

    /// <summary>损坏的项目或路线无法发布缓存，批准引用和旧内容保持不变。</summary>
    [Theory]
    [InlineData("javascript", "js/示例", "repo/js/示例/manifest.json")]
    [InlineData("pathing", "pathing/路线.json", "repo/pathing/路线.json")]
    public async Task InvalidContent_CannotBeApproved(string type, string relative, string file)
    {
        var store = CreateStore();
        var source = CreateSource("invalid");
        var reference = await ReferenceAsync(store, source, relative);
        var oldCache = await store.MaterializeAsync(reference, type);
        var task = new PuloniaTask { TaskType = type, Resource = reference };
        var service = new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog(store));
        Write(source, file, "{invalid-json");
        Commit(source);
        var current = await service.ReadCurrentStateAsync(task, []);
        await Assert.ThrowsAnyAsync<Exception>(() => service.PrepareUpdateAsync(task, current));
        Assert.Same(reference, task.Resource);
        Assert.Equal(oldCache, await store.MaterializeAsync(reference, type));
        Assert.Empty(Directory.GetDirectories(Path.Combine(store.RootDirectory, "Cache", reference.RepositoryId), "*.tmp", SearchOption.AllDirectories));
    }

    /// <summary>审阅后仅模块发生变化也必须重新核对提交，不能用相同指纹确认未审阅的依赖。</summary>
    [Fact]
    public async Task Confirmation_RejectsUnreviewedRevision_WithSameFingerprint()
    {
        var store = CreateStore();
        var source = CreateSource("revision");
        var reference = await ReferenceAsync(store, source);
        var task = new PuloniaTask { TaskType = "javascript", Resource = reference };
        var service = new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog(store));
        Write(source, "repo/js/示例/main.js", "// 待确认入口");
        Commit(source);
        var reviewed = await service.ReadCurrentStateAsync(task, []);
        Write(source, "repo/js/示例/helper.js", "// 未审阅模块");
        Commit(source);
        var changed = await service.ReadCurrentStateAsync(task, []);
        Assert.Equal(reviewed.CurrentContentVersion, changed.CurrentContentVersion);
        Assert.NotEqual(reviewed.CurrentRepositoryRevision, changed.CurrentRepositoryRevision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareUpdateAsync(task, reviewed));
        Assert.Same(reference, task.Resource);
    }

    /// <summary>ZIP 更新时间、大小不变时由持久化代次通知其他实例，事件携带确切版本。</summary>
    [Fact]
    public async Task FileRepository_UpdateGenerationInvalidatesOtherInstance()
    {
        var reader = CreateStore();
        var writer = CreateStore();
        var source = CreateSource("offline", false);
        var reference = await ReferenceAsync(reader, source, "pathing/路线.json");
        await reader.MaterializeAsync(reference, "pathing");
        writer.NotifyUpdated(source);
        using (var unchanged = await reader.OpenCurrentAsync(reference.RepositoryId))
            Assert.Equal(reference.ApprovedRevision, unchanged.Revision);
        var path = Path.Combine(source, "repo/pathing/路线.json");
        var timestamp = File.GetLastWriteTimeUtc(path);
        Write(source, "repo/pathing/路线.json", "{\"route\":3}");
        File.SetLastWriteTimeUtc(path, timestamp);
        ScriptRepositoryChangedEventArgs? notification = null;
        writer.RepositoryChanged += (_, args) => notification = args;
        writer.NotifyUpdated(source);
        using var latest = await reader.OpenCurrentAsync(reference.RepositoryId);
        Assert.NotEqual(reference.ApprovedRevision, latest.Revision);
        Assert.Equal(latest.Revision, notification?.Revision);
        Assert.Equal(reference.RepositoryId, notification?.RepositoryId);
        using var old = await reader.OpenApprovedAsync(reference);
        Assert.Equal("{\"route\":1}", old.ReadText("repo/pathing/路线.json"));
    }

    /// <summary>来源删除资源后更新检查明确失败，批准的旧资源仍可提取和运行。</summary>
    [Fact]
    public async Task RemovedSourceResource_KeepsApprovedContent()
    {
        var store = CreateStore();
        var source = CreateSource("removed");
        var reference = await ReferenceAsync(store, source, "pathing/路线.json");
        await store.MaterializeAsync(reference, "pathing");
        File.Delete(Path.Combine(source, "repo/pathing/路线.json"));
        Commit(source);
        var service = new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog(store));
        var task = new PuloniaTask { TaskType = "pathing", Resource = reference };
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ReadCurrentVersionAsync(task, []));
        Assert.Equal("{\"route\":1}", File.ReadAllText(await store.MaterializeAsync(reference, "pathing")));
    }

    /// <summary>列表只读取发布索引，预览仅有三份文档，未选资源和完整版本均不被提取。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BrowsingAndPreview_DoNotRetainRepositoryOrExtractCode(bool git)
    {
        var store = CreateStore();
        var source = CreateSource("lightweight", git);
        var manifest = JObject.Parse(ScriptManifest);
        manifest["settings_ui"] = "settings/form.json";
        Write(source, "repo/js/示例/manifest.json", manifest.ToString());
        Write(source, "repo/js/示例/settings/form.json", "[]");
        Write(source, "repo/js/示例/README.md", "# preview");
        Write(source, "repo/js/未索引/manifest.json", ScriptManifest);
        Write(source, "repo/js/未索引/main.js", new string('x', 1024 * 1024));
        if (git) Commit(source);
        var catalog = new PuloniaTaskResourceCatalog(store);
        var index = await catalog.GetIndexAsync(new PuloniaTaskDefinition { TaskType = "javascript" });
        var resource = Assert.Single(index.Resources);
        Assert.Equal("示例", resource.DisplayName);
        var preview = await PuloniaTaskResourcePreviewLoader.LoadAsync(resource, "javascript", repositories: store);
        Assert.NotNull(preview.MarkdownFilePath);
        var files = Directory.GetFiles(Path.Combine(store.RootDirectory, "Previews"), "*", SearchOption.AllDirectories);
        Assert.Equal(3, files.Length);
        Assert.DoesNotContain(files, file => file.EndsWith("main.js", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(store.RootDirectory, "Snapshots")));
        Assert.False(Directory.Exists(Path.Combine(store.RootDirectory, "Cache")));
    }

    /// <summary>路线只保留选中的正文；删除来源与缓存后仍能从资源归档恢复。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RouteArchive_RetainsOnlySelectedFile(bool git)
    {
        var store = CreateStore();
        var source = CreateSource("route-only", git);
        var reference = await ReferenceAsync(store, source, "pathing/路线.json");
        var cache = await store.MaterializeAsync(reference, "pathing");
        using (var snapshot = await store.OpenApprovedAsync(reference))
            Assert.Equal("repo/pathing/路线.json", Assert.Single(snapshot.EnumerateFiles("")).Path);
        Assert.False(Directory.Exists(Path.Combine(store.RootDirectory, "Snapshots", reference.RepositoryId, "objects.git")));
        Assert.Single(Directory.GetFiles(Path.Combine(store.RootDirectory, "Snapshots", reference.RepositoryId, "Archives"), "*.zip"));
        DeleteTemporary(source);
        DeleteTemporary(Path.GetDirectoryName(Path.GetDirectoryName(cache))!);
        Assert.Equal("{\"route\":1}", File.ReadAllText(await store.MaterializeAsync(reference, "pathing")));
    }

    /// <summary>JS 归档只保留所选项目和运行依赖，跨提交复用没有变化的公共块。</summary>
    [Fact]
    public async Task ScriptArchive_ExcludesOtherResources_AndReusesCommonBlocks()
    {
        var store = CreateStore();
        var source = CreateSource("chunks");
        Write(source, "repo/js/其他/main.js", "// 未选择项目");
        Write(source, "repo/js/其他/manifest.json", ScriptManifest);
        Write(source, "unrelated.txt", "unrelated");
        Commit(source);
        var reference = await ReferenceAsync(store, source);
        await store.MaterializeAsync(reference, "javascript");
        using (var approved = await store.OpenApprovedAsync(reference))
        {
            Assert.False(approved.FileExists("repo/js/其他/main.js"));
            Assert.False(approved.FileExists("repo.json"));
            Assert.False(approved.FileExists("unrelated.txt"));
            Assert.True(approved.FileExists("repo/js/示例/helper.js"));
            Assert.True(approved.FileExists("packages/shared.js"));
            Assert.True(approved.FileExists("repo/pathing/子目录/第二条.json"));
        }
        var snapshotRoot = Path.Combine(store.RootDirectory, "Snapshots", reference.RepositoryId);
        var oldScopes = JObject.Parse(File.ReadAllText(Path.Combine(snapshotRoot, reference.ApprovedRevision, "scopes.json")));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(snapshotRoot, "Archives"), "*.zip").Length);
        Write(source, "repo/js/示例/main.js", "// 新版本入口");
        Commit(source);
        var next = await ReferenceAsync(store, source);
        await store.MaterializeAsync(next, "javascript");
        var newScopes = JObject.Parse(File.ReadAllText(Path.Combine(snapshotRoot, next.ApprovedRevision, "scopes.json")));
        Assert.Equal(oldScopes.Value<string>("packages"), newScopes.Value<string>("packages"));
        Assert.Equal(oldScopes.Value<string>("repo/pathing"), newScopes.Value<string>("repo/pathing"));
        Assert.Equal(4, Directory.GetFiles(Path.Combine(snapshotRoot, "Archives"), "*.zip").Length);
        Assert.False(Directory.Exists(Path.Combine(snapshotRoot, "objects.git")));
    }

    /// <summary>已经存在的旧版整仓 Git/ZIP 快照仍可离线读取，不做破坏性迁移。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LegacyWholeRepositorySnapshots_RemainReadable(bool git)
    {
        var store = CreateStore();
        var source = CreateSource("legacy", git);
        var reference = await ReferenceAsync(store, source);
        var snapshotRoot = Path.Combine(store.RootDirectory, "Snapshots", reference.RepositoryId);
        Directory.CreateDirectory(snapshotRoot);
        if (git)
        {
            var legacyPath = Path.Combine(snapshotRoot, "objects.git");
            Repository.Clone(source, legacyPath, new CloneOptions { IsBare = true });
            using var repository = new Repository(legacyPath);
            repository.Refs.Add("refs/pulonia/revisions/" + reference.ApprovedRevision, repository.Head.Tip.Id);
        }
        else ZipFile.CreateFromDirectory(source, Path.Combine(snapshotRoot, reference.ApprovedRevision + ".zip"));
        DeleteTemporary(source);
        var cache = await store.MaterializeAsync(reference, "javascript");
        Assert.Equal("// 初始版本", File.ReadAllText(Path.Combine(cache, "main.js")));
        using var approved = await store.OpenApprovedAsync(reference);
        Assert.Equal("{\"route\":1}", approved.ReadText("repo/pathing/路线.json"));
    }

    /// <summary>只清理本测试的绝对临时目录，并先解除 Git 对象的只读属性。</summary>
    private void DeleteTemporary(string path)
    {
        var absolute = Path.GetFullPath(path);
        if (!absolute.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && absolute != Path.GetFullPath(_root)) throw new InvalidOperationException("拒绝删除测试目录外的位置。");
        if (!Directory.Exists(absolute)) return;
        foreach (var file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(absolute, true);
    }

    /// <summary>测试结束释放所有临时资源，不接触真实用户目录。</summary>
    public void Dispose() => DeleteTemporary(_root);
}
