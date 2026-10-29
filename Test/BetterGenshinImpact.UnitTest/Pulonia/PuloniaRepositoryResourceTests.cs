using System.Text;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.UnitTest.GameTaskTests;
using Vanara.PInvoke;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Behavior;
using LibGit2Sharp;
using Newtonsoft.Json.Linq;
using System.IO.Compression;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>通过真实临时 Git 对象库和离线文件仓库验证资源隔离，不启动游戏。</summary>
public sealed class PuloniaRepositoryResourceTests : IDisposable
{
    /// <summary>每个测试独立的临时根目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));
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

    /// <summary>读取已确认版本中的一个文本文件，不经过任何提取缓存。</summary>
    private static async Task<string> ReadApprovedAsync(ScriptRepositoryStore store, ScriptResourceReference reference, string path)
    {
        using var snapshot = await store.OpenApprovedAsync(reference);
        return snapshot.ReadText(path);
    }

    /// <summary>启动清理只删除空闲的可读名锁，保留活跃锁和非锁文件。</summary>
    [Fact]
    public void LockCleanup_RemovesOnlyInactiveLocks()
    {
        var store = CreateStore();
        var directory = Path.Combine(store.RootDirectory, "Locks");
        Directory.CreateDirectory(directory);
        var stale = Path.Combine(directory, "registry.lock");
        var active = Path.Combine(directory, "retain.rep_0123456789abcdef.lock");
        var foreign = Path.Combine(directory, "readme.txt");
        File.WriteAllBytes(stale, []);
        File.WriteAllBytes(active, []);
        File.WriteAllBytes(foreign, []);

        using var activeGate = new FileStream(active, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(1, store.CleanupStaleLockFiles());
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(active));
        Assert.True(File.Exists(foreign));
    }

    /// <summary>新注册使用短仓库 ID，官方固定为 official，引用校验拒绝旧 32 位 GUID。</summary>
    [Fact]
    public async Task RepositoryIds_AreShortAndReadable()
    {
        var store = CreateStore();
        var registration = await store.RegisterAsync(CreateSource("short-id", false));
        Assert.Matches("^rep_[0-9a-hjkmnp-tv-z]{16}$", registration.Id);
        Assert.Equal("official", (await store.EnsureOfficialRepositoryAsync()).Id);
        Assert.True(ScriptResourceReference.IsValidRepositoryId(registration.Id));
        Assert.False(ScriptResourceReference.IsValidRepositoryId(new string('a', 32)));
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
        // 导入入口也必须先校验，不能调用共享导入器或创建无效仓库注册。
        var service = new PuloniaRepositoryManagementService(store);
        await Assert.ThrowsAsync<IOException>(() => service.ImportLocalZipAsync(invalidZip));
        Assert.Empty((await store.GetRepositoriesAsync()).Where(repository => !repository.IsOfficial));
    }

    /// <summary>目录与 ZIP 自动选择正确操作，普通文件、快捷方式、缺失路径及多项拖入被拒绝。</summary>
    [Fact]
    public void LocalSource_RecognizesDirectoryAndZip_AndRejectsOtherInputs()
    {
        var source = CreateSource("local-source", false);
        var zip = Path.Combine(_root, "source.ZIP");
        ZipFile.CreateFromDirectory(source, zip);
        Assert.Equal(PuloniaLocalRepositorySourceKind.Directory,
            PuloniaRepositoryManagementService.ClassifyLocalSource(FileSystemDropBehavior.GetSinglePath([source])));
        Assert.Equal(PuloniaLocalRepositorySourceKind.Zip,
            PuloniaRepositoryManagementService.ClassifyLocalSource(FileSystemDropBehavior.GetSinglePath([zip])));
        Write(_root, "ordinary.txt", "not a repository");
        Write(_root, "shortcut.lnk", "not an actual directory");
        Assert.Throws<IOException>(() => PuloniaRepositoryManagementService.ClassifyLocalSource(Path.Combine(_root, "ordinary.txt")));
        Assert.Throws<IOException>(() => PuloniaRepositoryManagementService.ClassifyLocalSource(Path.Combine(_root, "shortcut.lnk")));
        Assert.Throws<IOException>(() => PuloniaRepositoryManagementService.ClassifyLocalSource(Path.Combine(_root, "missing.zip")));
        Assert.Throws<ArgumentException>(() => FileSystemDropBehavior.GetSinglePath([source, zip]));
    }

    /// <summary>统一目录添加入口在索引验证成功前不产生注册，通过后返回真实来源身份。</summary>
    [Fact]
    public async Task LocalDirectory_InvalidIndexDoesNotRegister()
    {
        var store = CreateStore();
        var service = new PuloniaRepositoryManagementService(store);
        var source = CreateSource("local-invalid", false);
        Write(source, "repo.json", "{}");
        await Assert.ThrowsAsync<IOException>(() => service.AddLocalSourceAsync(source));
        Assert.Empty((await store.GetRepositoriesAsync()).Where(repository => !repository.IsOfficial));
        Write(source, "repo.json", "{\"indexes\":[]}");
        var added = await service.AddLocalSourceAsync(source);
        Assert.Equal(source, added.Directory);
        Assert.Equal(added.Id, Assert.Single((await store.GetRepositoriesAsync()).Where(repository => !repository.IsOfficial)).Id);
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
        await store.RetainAsync(reference, "javascript");
        string originalVersion;
        using (var approved = await store.OpenApprovedAsync(reference))
            originalVersion = PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(approved, reference.RelativePath, "javascript");
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
        Assert.Equal("// 旧模块", await ReadApprovedAsync(store, reference, "repo/js/示例/helper.js"));
        Assert.Equal("// 旧公共包", await ReadApprovedAsync(store, reference, "packages/shared.js"));
    }

    /// <summary>重新克隆或移除当前来源不破坏旧版本；工作目录从归档重建，旧宿主 API 从确认时版本查询路线。</summary>
    [Fact]
    public async Task ApprovedSnapshot_SurvivesSourceDeletion_AndRebuildsWorkspace()
    {
        var store = CreateStore();
        var source = CreateSource("official");
        var reference = await ReferenceAsync(store, source);
        string workspacePath;
        using (var workspace = await store.AcquireWorkspaceAsync(reference))
            workspacePath = workspace.Path;
        DeleteTemporary(source);
        DeleteTemporary(workspacePath);
        using var restored = await store.AcquireWorkspaceAsync(reference);
        // 工作目录镜像仓库相对路径，便于用户直接找到脚本数据。
        Assert.Equal(Path.Combine(store.RootDirectory, "Workspaces", reference.RepositoryId, "js", "示例"), restored.Path);
        Assert.Equal("// 初始版本", File.ReadAllText(Path.Combine(restored.Path, "main.js")));
        Assert.Equal("// 旧公共包", File.ReadAllText(Path.Combine(restored.Path, "packages", "shared.js")));
        using var context = new ScriptRepositoryResourceContext(await store.OpenApprovedAsync(reference));
        var host = new AutoPathingScript(TestGameRuntime.Create(new FakeSystemInfo(new RECT(0, 0, 1920, 1080), 1)), new AllConfig(),
            restored.Path, null, CancellationToken.None, context);
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
        var first = CreateSource("official");
        var thirdParty = CreateSource("third-party");
        Write(thirdParty, "repo/pathing/路线.json", "{\"third_party\":true}");
        Commit(thirdParty);
        // 资源目录只列出明确注册的仓库，不自动发现 Repos 下的文件夹。
        await store.RegisterAsync(first);
        await store.RegisterAsync(thirdParty);
        var catalog = new PuloniaTaskResourceCatalog(store);
        var scripts = await catalog.GetIndexAsync(new PuloniaTaskDefinition { TaskType = "javascript" });
        Assert.Equal(2, scripts.Resources.Count);
        Assert.Equal(2, scripts.Resources.Select(r => r.Resource!.ScopeKey).Distinct().Count());
        var routes = await catalog.GetIndexAsync(new PuloniaTaskDefinition { TaskType = "pathing" });
        Assert.Equal(2, routes.Resources.Count(r => r.IsRootDirectory));
        var sameNames = routes.Resources.Where(r => r.DisplayName == "路线").ToArray();
        Assert.Equal(2, sameNames.Length);
        foreach (var item in sameNames) await store.RetainAsync(item.Resource!, "pathing");
        Assert.NotEqual(await ReadApprovedAsync(store, sameNames[0].Resource!, "repo/pathing/路线.json"),
            await ReadApprovedAsync(store, sameNames[1].Resource!, "repo/pathing/路线.json"));
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
        Assert.Equal("// 旧模块", await ReadApprovedAsync(store, reference, "repo/js/示例/helper.js"));
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
        await store.RetainAsync(reference, "pathing");
        using var approved = await store.OpenApprovedAsync(reference);
        var version = PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(approved, "pathing", "pathing", directory: true);
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
        Assert.All(restored.RootTask.Children.Single().Children, child =>
        {
            Assert.Equal(reference.ApprovedRevision, child.Resource!.ApprovedRevision);
            // 快照不记录本机缓存路径，展开节点 ID 是由相对路径派生的短 ID。
            Assert.Null(child.Path);
            Assert.Matches("^res_[0-9a-hjkmnp-tv-z]{16}$", child.TaskId);
        });
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
        await store.RegisterAsync(source);
        var catalog = new PuloniaTaskResourceCatalog(store);
        var index = await catalog.GetIndexAsync(new PuloniaTaskDefinition { TaskType = "javascript" });
        var resource = Assert.Single(index.Resources).Resource!;
        await store.RetainAsync(resource, "javascript");
        Assert.Equal("// 初始版本", await ReadApprovedAsync(store, resource, "repo/js/示例/main.js"));
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
        await store.RetainAsync(reference, type);
        var task = new PuloniaTask { TaskType = type, Resource = reference };
        var service = new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog(store));
        Write(source, file, "{invalid-json");
        Commit(source);
        var current = await service.ReadCurrentStateAsync(task, []);
        await Assert.ThrowsAnyAsync<Exception>(() => service.PrepareUpdateAsync(task, current));
        Assert.Same(reference, task.Resource);
        // 校验失败的新版本不登记任何归档，旧版本仍可读取，资源区不残留临时文件。
        var index = JObject.Parse(File.ReadAllText(Path.Combine(store.RootDirectory, "Snapshots", reference.RepositoryId, "retained.json")));
        Assert.Single(index.Value<JObject>("revisions")!.Properties());
        Assert.NotEmpty(await ReadApprovedAsync(store, reference, file));
        Assert.Empty(Directory.GetFileSystemEntries(store.RootDirectory, "~*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(store.RootDirectory, "Cache")));
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

    /// <summary>
    /// ZIP 导入重写 repo_updated.json；即使正文替换后大小、时间不变，其他实例也得到新修订并丢弃旧哈希缓存。
    /// 没有内容变化的通知不改变修订，事件携带确切版本，旧版本仍可读取。
    /// </summary>
    [Fact]
    public async Task FileRepository_ImportMarkerInvalidatesOtherInstance()
    {
        var reader = CreateStore();
        var writer = CreateStore();
        var source = CreateSource("offline", false);
        Write(source, "repo_updated.json", "{\"indexes\":[]}");
        var reference = await ReferenceAsync(reader, source, "pathing/路线.json");
        await reader.RetainAsync(reference, "pathing");
        writer.NotifyUpdated(source);
        using (var unchanged = await reader.OpenCurrentAsync(reference.RepositoryId))
            Assert.Equal(reference.ApprovedRevision, unchanged.Revision);

        // 模拟另一实例导入 ZIP：正文等长替换并恢复时间，导入器随后重写更新标记。
        var path = Path.Combine(source, "repo/pathing/路线.json");
        var timestamp = File.GetLastWriteTimeUtc(path);
        Write(source, "repo/pathing/路线.json", "{\"route\":3}");
        File.SetLastWriteTimeUtc(path, timestamp);
        var marker = Path.Combine(source, "repo_updated.json");
        Write(source, "repo_updated.json", "{\"indexes\":[]}");
        File.SetLastWriteTimeUtc(marker, File.GetLastWriteTimeUtc(marker).AddSeconds(5));
        ScriptRepositoryChangedEventArgs? notification = null;
        writer.RepositoryChanged += (_, args) => notification = args;
        writer.NotifyUpdated(source);

        using (var latest = await reader.OpenCurrentAsync(reference.RepositoryId))
        {
            Assert.NotEqual(reference.ApprovedRevision, latest.Revision);
            Assert.Equal(latest.Revision, notification?.Revision);
            Assert.Equal(reference.RepositoryId, notification?.RepositoryId);
            // 读取方的旧哈希缓存随导入标记失效，不会把新正文误判为“读取期间来源变化”。
            Assert.Equal("{\"route\":3}", latest.ReadText("repo/pathing/路线.json"));
        }
        using var old = await reader.OpenApprovedAsync(reference);
        Assert.Equal("{\"route\":1}", old.ReadText("repo/pathing/路线.json"));
    }

    /// <summary>来源锁按仓库目录区分：一个仓库正在同步时，另一个仓库仍可打开读取。</summary>
    [Fact]
    public async Task SourceLock_IsPerRepository()
    {
        var store = CreateStore();
        var syncing = await store.RegisterAsync(CreateSource("syncing", false));
        var browsing = await store.RegisterAsync(CreateSource("browsing", false));
        using (await store.EnterSourceAccessAsync(syncing.Directory))
        {
            using (var other = await store.OpenCurrentAsync(browsing.Id).WaitAsync(TimeSpan.FromSeconds(5)))
                Assert.True(other.FileExists("repo/pathing/路线.json"));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.OpenCurrentAsync(syncing.Id, cancellation.Token));
        }
        using var released = await store.OpenCurrentAsync(syncing.Id).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(released.FileExists("repo/pathing/路线.json"));
    }

    /// <summary>注册与选择偏好的读取不取锁：另一实例持有 registry 写锁时，浏览仍可继续。</summary>
    [Fact]
    public async Task RegistryAndSelectionReads_DoNotWaitForWriterLock()
    {
        var store = CreateStore();
        var registration = await store.RegisterAsync(CreateSource("readers", false));
        await store.SelectRepositoryAsync("pathing", registration.Id);
        // 官方记录已经补入后，列出仓库只读取注册文件。
        await store.EnsureOfficialRepositoryAsync();
        var locks = Path.Combine(store.RootDirectory, "Locks");
        Directory.CreateDirectory(locks);
        using (new FileStream(Path.Combine(locks, "registry.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Contains((await store.GetRepositoriesAsync().WaitAsync(TimeSpan.FromSeconds(5))), r => r.Id == registration.Id);
            Assert.Equal(registration.Id, await store.GetSelectedRepositoryIdAsync("pathing").WaitAsync(TimeSpan.FromSeconds(5)));
            using var snapshot = await store.OpenCurrentAsync(registration.Id).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(snapshot.FileExists("repo.json"));
        }
    }

    /// <summary>同一脚本的工作目录被占用时，超过等待时间后明确报错，而不是无限等待。</summary>
    [Fact]
    public async Task WorkspaceLock_TimesOutWithClearMessage()
    {
        var store = new ScriptRepositoryStore(Path.Combine(_root, "resources"), Path.Combine(_root, "repos"))
        { WorkspaceLockTimeout = TimeSpan.FromMilliseconds(300) };
        var reference = await ReferenceAsync(store, CreateSource("busy-script"));
        using (await store.AcquireWorkspaceAsync(reference))
        {
            var error = await Assert.ThrowsAsync<IOException>(() => store.AcquireWorkspaceAsync(reference))
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("另一个运行", error.Message);
            Assert.Contains(reference.RelativePath, error.Message);
        }
        using var released = await store.AcquireWorkspaceAsync(reference);
        Assert.True(File.Exists(Path.Combine(released.Path, "main.js")));
    }

    /// <summary>批量检查按项返回结果：同一仓库的缺失资源只影响自身，其余结果与单项检查一致。</summary>
    [Fact]
    public async Task BatchCheck_ReturnsPerTaskResults()
    {
        var store = CreateStore();
        var source = CreateSource("batch");
        var script = await ReferenceAsync(store, source);
        var route = script.WithPath("pathing/路线.json");
        var missing = script.WithPath("pathing/缺失.json");
        await store.RetainAsync(script, "javascript");
        var tasks = new[]
        {
            new PuloniaTask { TaskType = "javascript", Resource = script },
            new PuloniaTask { TaskType = "pathing", Resource = missing },
            new PuloniaTask { TaskType = "pathing", Resource = route }
        };
        var service = new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog(store));
        var results = await service.ReadCurrentStatesAsync(tasks, []);
        Assert.Equal(3, results.Count);
        Assert.Equal(await service.ReadCurrentVersionAsync(tasks[0], []), results[0].State!.CurrentContentVersion);
        Assert.Equal("1", results[0].State!.ApprovedDeclaredVersion);
        Assert.IsType<FileNotFoundException>(results[1].Error);
        Assert.Null(results[1].State);
        Assert.Equal(await service.ReadCurrentVersionAsync(tasks[2], []), results[2].State!.CurrentContentVersion);
    }

    /// <summary>来源删除资源后更新检查明确失败，批准的旧资源仍可提取和运行。</summary>
    [Fact]
    public async Task RemovedSourceResource_KeepsApprovedContent()
    {
        var store = CreateStore();
        var source = CreateSource("removed");
        var reference = await ReferenceAsync(store, source, "pathing/路线.json");
        await store.RetainAsync(reference, "pathing");
        File.Delete(Path.Combine(source, "repo/pathing/路线.json"));
        Commit(source);
        var service = new PuloniaTaskResourceVersionService(new PuloniaTaskResourceCatalog(store));
        var task = new PuloniaTask { TaskType = "pathing", Resource = reference };
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ReadCurrentVersionAsync(task, []));
        Assert.Equal("{\"route\":1}", await ReadApprovedAsync(store, reference, "repo/pathing/路线.json"));
    }

    /// <summary>路线只保留选中的正文；删除来源后仍能直接从资源归档读取，不生成提取缓存。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RouteArchive_RetainsOnlySelectedFile(bool git)
    {
        var store = CreateStore();
        var source = CreateSource("route-only", git);
        var reference = await ReferenceAsync(store, source, "pathing/路线.json");
        await store.RetainAsync(reference, "pathing");
        using (var snapshot = await store.OpenApprovedAsync(reference))
            Assert.Equal("repo/pathing/路线.json", Assert.Single(snapshot.EnumerateFiles("")).Path);
        var snapshotRoot = Path.Combine(store.RootDirectory, "Snapshots", reference.RepositoryId);
        var block = Assert.Single(Directory.GetFiles(snapshotRoot, "*.zip"));
        Assert.Matches("^blk_[0-9a-hjkmnp-tv-z]{16}$", Path.GetFileNameWithoutExtension(block));
        Assert.True(File.Exists(Path.Combine(snapshotRoot, "retained.json")));
        Assert.False(Directory.Exists(Path.Combine(store.RootDirectory, "Cache")));
        DeleteTemporary(source);
        Assert.Equal("{\"route\":1}", await ReadApprovedAsync(store, reference, "repo/pathing/路线.json"));
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
