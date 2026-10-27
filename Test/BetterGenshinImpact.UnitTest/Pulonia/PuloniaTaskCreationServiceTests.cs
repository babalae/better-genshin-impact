using System.Text;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 新增任务构建服务：参数合并校验、资源指纹与三种目录添加方式。只使用临时目录和文件式仓库，不依赖 WPF。
/// </summary>
public sealed class PuloniaTaskCreationServiceTests : IDisposable
{
    /// <summary>单文件 <c>{"route":1}</c> 的内容指纹，与 PuloniaTaskResourceFingerprintTests 一致。</summary>
    private const string RouteVersion = "26b5fe4288b5b4de0832e487c1f09906a8eb9a57f4360d522153f2c537846757";

    /// <summary>目录 a.json 与 子目录/b.json 的清单指纹，与 PuloniaTaskResourceFingerprintTests 一致。</summary>
    private const string DirectoryVersion = "b7a7cb94f6135cfa88005250192af7497fd08a6fb8f11ac0726812d7bc864109";

    /// <summary>每个测试独占的临时目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>测试文件使用 UTF-8 无 BOM，保证字节输入固定。</summary>
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>无资源任务只保存名称与显式参数，默认值不会写入节点。</summary>
    [Fact]
    public async Task ResourceFreeTask_KeepsOnlyOverrides()
    {
        var service = new PuloniaTaskCreationService(new PuloniaTaskResourceCatalog(CreateStore()));
        var overrides = new JObject { ["file_name"] = "cmd.exe" };

        var task = await service.BuildAsync(new PuloniaTaskCreationRequest { Name = "命令", Definition = ShellDefinition(), Overrides = overrides });

        Assert.Equal("命令", task.Name);
        Assert.Equal("shell", task.TaskType);
        Assert.Null(task.Path);
        Assert.Null(task.ResourceVersion);
        Assert.True(JToken.DeepEquals(overrides, task.Parameters));
    }

    /// <summary>合并默认值后仍为空的必填命令、以及不符合 Schema 的参数都在读取资源前拒绝。</summary>
    [Fact]
    public async Task InvalidParameters_AreRejectedBeforeBuilding()
    {
        var service = new PuloniaTaskCreationService(new PuloniaTaskResourceCatalog(CreateStore()));

        var empty = await Assert.ThrowsAsync<FormatException>(() => service.BuildAsync(
            new PuloniaTaskCreationRequest { Name = "命令", Definition = ShellDefinition() }));
        Assert.Equal("“程序或命令”不能为空。", empty.Message);
        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => service.BuildAsync(new PuloniaTaskCreationRequest
        {
            Name = "命令", Definition = ShellDefinition(), Overrides = new JObject { ["file_name"] = 5 }
        }));
    }

    /// <summary>只有路线和录制目录按批量添加处理，JS 项目目录仍是单个资源。</summary>
    [Fact]
    public void DirectoryImport_OnlyForPathingAndKeyMouseDirectories()
    {
        var directory = LocalResource(_root, isDirectory: true);
        Assert.True(PuloniaTaskCreationService.IsDirectoryImport("pathing", directory));
        Assert.True(PuloniaTaskCreationService.IsDirectoryImport("keymouse", directory));
        Assert.False(PuloniaTaskCreationService.IsDirectoryImport("javascript", directory));
        Assert.False(PuloniaTaskCreationService.IsDirectoryImport("keymouse", LocalResource(Path.Combine(_root, "a.json"), isDirectory: false)));
        Assert.False(PuloniaTaskCreationService.IsDirectoryImport("pathing", null));
    }

    /// <summary>本地录制文件保存相对路径和固定指纹。</summary>
    [Fact]
    public async Task LocalFile_UsesFileFingerprint()
    {
        var root = Path.Combine(_root, "local");
        Write(root, "route.json", "{\"route\":1}");
        var service = new PuloniaTaskCreationService(new PuloniaTaskResourceCatalog(CreateStore()));
        var resource = LocalResource(Path.Combine(root, "route.json"), isDirectory: false, relativePath: "route.json");

        var task = await service.BuildAsync(new PuloniaTaskCreationRequest
        {
            Name = "录制", Definition = KeyMouseDefinition(root), Resource = resource
        });

        Assert.Equal("keymouse", task.TaskType);
        Assert.Equal("route.json", task.Path);
        Assert.Equal(RouteVersion, task.ResourceVersion);
    }

    /// <summary>本地目录引用保存固定清单指纹，表单中的共享设置写在引用分组上。</summary>
    [Fact]
    public async Task LocalDirectoryReference_PinsManifestAndGroupOverrides()
    {
        var root = CreateLocalDirectory();
        var service = new PuloniaTaskCreationService(new PuloniaTaskResourceCatalog(CreateStore()));
        var definition = KeyMouseDefinition(root);

        var task = await service.BuildAsync(new PuloniaTaskCreationRequest
        {
            Name = "目录", Definition = definition, Resource = LocalResource(Path.Combine(root, "dir"), isDirectory: true),
            Overrides = new JObject { ["with_delay"] = false }
        });

        Assert.Equal("group", task.TaskType);
        Assert.Empty(task.Children);
        Assert.Equal("directory", task.Source!.Kind);
        Assert.Equal(Path.Combine(root, "dir"), task.Source.Path);
        Assert.Equal("keymouse", task.Source.TaskType);
        Assert.True(task.Source.Recursive);
        Assert.Equal(DirectoryVersion, task.Source.Version);
        // 回放方式是共享设置，写在引用分组上由展开的录制继承
        Assert.Single(task.Settings);
        Assert.False(task.Settings.Value<bool>("keymouse.with_delay"));
        Assert.Empty(task.Parameters);
    }

    /// <summary>结构化展开按子目录建立分组，扁平展开放入同一分组；逐项回调进度。</summary>
    [Fact]
    public async Task LocalDirectoryExpansion_BuildsStructuredOrFlatTree()
    {
        var root = CreateLocalDirectory();
        var service = new PuloniaTaskCreationService(new PuloniaTaskResourceCatalog(CreateStore()));
        var resource = LocalResource(Path.Combine(root, "dir"), isDirectory: true);
        var progress = new List<(int Current, int Total)>();

        var structured = await service.BuildAsync(new PuloniaTaskCreationRequest
        {
            Name = "目录", Definition = KeyMouseDefinition(root), Resource = resource,
            DirectoryImportMode = PuloniaTaskDirectoryImportMode.Structured
        }, (current, total) => progress.Add((current, total)));

        Assert.Equal([(1, 2), (2, 2)], progress);
        Assert.Empty(structured.Settings);
        Assert.Equal(["a", "子目录"], structured.Children.Select(child => child.Name));
        Assert.Equal(Path.Combine("dir", "a.json"), structured.Children[0].Path);
        var nested = Assert.Single(structured.Children[1].Children);
        Assert.Equal("b", nested.Name);
        Assert.Equal(PuloniaTaskResourceFingerprint.ComputeContentVersion(Utf8.GetBytes("{\"b\":2}")), nested.ResourceVersion);

        var flat = await service.BuildAsync(new PuloniaTaskCreationRequest
        {
            Name = "目录", Definition = KeyMouseDefinition(root), Resource = resource,
            DirectoryImportMode = PuloniaTaskDirectoryImportMode.Flat, IncludeSubdirectories = false
        });
        Assert.Equal(["a"], flat.Children.Select(child => child.Name));
    }

    /// <summary>没有 JSON 资源的目录不能生成空分组。</summary>
    [Fact]
    public async Task EmptyDirectory_IsRejected()
    {
        var root = Path.Combine(_root, "local");
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        var service = new PuloniaTaskCreationService(new PuloniaTaskResourceCatalog(CreateStore()));

        await Assert.ThrowsAsync<FormatException>(() => service.BuildAsync(new PuloniaTaskCreationRequest
        {
            Name = "空目录", Definition = KeyMouseDefinition(root), Resource = LocalResource(Path.Combine(root, "empty"), isDirectory: true)
        }));
    }

    /// <summary>仓库资源先保留所选版本，单文件、目录引用与展开的指纹与本地算法一致。</summary>
    [Fact]
    public async Task RepositoryResources_RetainVersionAndMatchFingerprints()
    {
        var store = CreateStore();
        var registration = await store.RegisterAsync(CreateRepositorySource());
        var catalog = new PuloniaTaskResourceCatalog(store);
        var definition = new PuloniaTaskDefinition { TaskType = "pathing" };
        var index = await catalog.GetIndexAsync(definition, repositoryId: registration.Id);
        var service = new PuloniaTaskCreationService(catalog);
        var route = index.Resources.Single(item => item.Resource!.RelativePath == "pathing/route.json");
        var directory = index.Resources.Single(item => item.Resource!.RelativePath == "pathing/dir");

        var leaf = await service.BuildAsync(new PuloniaTaskCreationRequest { Name = "路线", Definition = definition, Resource = route });
        Assert.Same(route.Resource, leaf.Resource);
        Assert.Equal(RouteVersion, leaf.ResourceVersion);

        var reference = await service.BuildAsync(new PuloniaTaskCreationRequest { Name = "目录", Definition = definition, Resource = directory });
        Assert.Equal("directory", reference.Source!.Kind);
        Assert.Same(directory.Resource, reference.Source.Resource);
        Assert.Equal(DirectoryVersion, reference.Source.Version);

        var expanded = await service.BuildAsync(new PuloniaTaskCreationRequest
        {
            Name = "目录", Definition = definition, Resource = directory, DirectoryImportMode = PuloniaTaskDirectoryImportMode.Structured
        });
        Assert.Equal("pathing/dir/a.json", expanded.Children[0].Resource!.RelativePath);
        Assert.Equal(directory.Resource!.ApprovedRevision, expanded.Children[0].Resource!.ApprovedRevision);
        var nested = Assert.Single(expanded.Children.Single(child => child.TaskType == "group").Children);
        Assert.Equal("pathing/dir/子目录/b.json", nested.Resource!.RelativePath);
        Assert.Equal(PuloniaTaskResourceFingerprint.ComputeContentVersion(Utf8.GetBytes("{\"b\":2}")), nested.ResourceVersion);
    }

    /// <summary>Shell 能力：命令默认为空，Schema 声明为字符串。</summary>
    private static PuloniaTaskDefinition ShellDefinition() => new()
    {
        TaskType = "shell",
        DefaultParameters = new JObject { ["file_name"] = "" },
        ParameterSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["file_name"] = new JObject { ["type"] = "string" } }
        }
    };

    /// <summary>录制回放能力，资源根目录为测试目录。</summary>
    private static PuloniaTaskDefinition KeyMouseDefinition(string root) => new()
    {
        TaskType = "keymouse",
        ResourceBaseDirectory = root,
        DefaultParameters = new JObject { ["with_delay"] = false },
        ParameterSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["with_delay"] = new JObject { ["type"] = "boolean" } }
        },
        SettingBindings = new Dictionary<string, string> { ["keymouse.with_delay"] = "with_delay" }
    };

    /// <summary>建立一项本地资源描述，模拟本地资源索引的结果。</summary>
    private static PuloniaTaskResourceDescriptor LocalResource(string fullPath, bool isDirectory, string? relativePath = null)
        => new(Path.GetFileNameWithoutExtension(fullPath), relativePath ?? Path.GetFileName(fullPath), fullPath,
            string.Empty, isDirectory, false, 0, DateTime.UtcNow, 0);

    /// <summary>写入本地目录资源：dir/a.json 与 dir/子目录/b.json。</summary>
    private string CreateLocalDirectory()
    {
        var root = Path.Combine(_root, "local");
        Write(root, "dir/a.json", "{\"a\":1}");
        Write(root, "dir/子目录/b.json", "{\"b\":2}");
        return root;
    }

    /// <summary>建立文件式来源：根目录一条路线，dir 目录下与本地目录相同的两份资源。</summary>
    private string CreateRepositorySource()
    {
        var source = Path.Combine(_root, "repos", "source");
        Write(source, "repo.json", new JObject
        {
            ["indexes"] = new JArray
            {
                new JObject { ["name"] = "pathing", ["type"] = "directory", ["children"] = new JArray
                {
                    new JObject { ["name"] = "route.json", ["type"] = "file" },
                    new JObject { ["name"] = "dir", ["type"] = "directory", ["children"] = new JArray
                    {
                        new JObject { ["name"] = "a.json", ["type"] = "file" },
                        new JObject { ["name"] = "子目录", ["type"] = "directory", ["children"] = new JArray
                        { new JObject { ["name"] = "b.json", ["type"] = "file" } } }
                    } }
                } }
            }
        }.ToString());
        Write(source, "repo/pathing/route.json", "{\"route\":1}");
        Write(source, "repo/pathing/dir/a.json", "{\"a\":1}");
        Write(source, "repo/pathing/dir/子目录/b.json", "{\"b\":2}");
        return source;
    }

    /// <summary>建立隔离的仓库存储。</summary>
    private ScriptRepositoryStore CreateStore() => new(Path.Combine(_root, "resources"), Path.Combine(_root, "repos"));

    /// <summary>写入一个普通 UTF-8 文件。</summary>
    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Utf8);
    }

    /// <summary>清理本测试的临时目录。</summary>
    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}
