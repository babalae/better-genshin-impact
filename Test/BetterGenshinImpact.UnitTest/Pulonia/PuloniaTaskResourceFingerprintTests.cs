using System.Text;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using LibGit2Sharp;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 固定资源指纹的逐字节输出。已保存计划中的 resource_version 依赖这些值，算法调整不能改变结果。
/// </summary>
public sealed class PuloniaTaskResourceFingerprintTests : IDisposable
{
    /// <summary>单文件 <c>{"route":1}</c> 的内容指纹。</summary>
    private const string RouteVersion = "26b5fe4288b5b4de0832e487c1f09906a8eb9a57f4360d522153f2c537846757";

    /// <summary>JS 项目只由 main.js 与 manifest.json 决定的指纹。</summary>
    private const string JavaScriptVersion = "d2d26ec53a999404243ec414a9e7da58bb0f30b1daa5d7796a7f211eed369564";

    /// <summary>目录 a.json 与 子目录/b.json 的清单指纹。</summary>
    private const string DirectoryVersion = "b7a7cb94f6135cfa88005250192af7497fd08a6fb8f11ac0726812d7bc864109";

    /// <summary>每个测试独占的临时目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>测试文件使用 UTF-8 无 BOM，保证字节输入固定。</summary>
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>本地文件、JS 项目和目录的指纹保持既有输出。</summary>
    [Fact]
    public async Task LocalFingerprints_MatchFixedValues()
    {
        var root = CreateContent(Path.Combine(_root, "local"));
        Assert.Equal(RouteVersion, await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(Path.Combine(root, "route.json")));
        Assert.Equal(JavaScriptVersion, await PuloniaTaskResourceFingerprint.ComputeJavaScriptVersionAsync(Path.Combine(root, "demo")));
        var directory = Path.Combine(root, "dir");
        var files = await new PuloniaTaskResourceCatalog().GetDirectoryFilesAsync(directory, "*.json", true);
        Assert.Equal(DirectoryVersion, await PuloniaTaskResourceFingerprint.ComputeDirectoryVersionAsync(directory, files));
    }

    /// <summary>运行准备对本地 JS 与目录引用计算的指纹与创建时一致。</summary>
    [Fact]
    public async Task BuilderFingerprints_MatchFixedValues()
    {
        var root = CreateContent(Path.Combine(_root, "local"));
        using var store = new PuloniaTaskStore(Path.Combine(_root, "plans"));
        var plan = new PuloniaTaskPlan { Name = "指纹", RootTask = new PuloniaTask { Name = "根", Children =
        [
            new PuloniaTask { Name = "脚本", TaskType = "javascript", Path = "demo" },
            new PuloniaTask { Name = "路线", TaskType = "pathing", Path = "route.json" },
            new PuloniaTask { Name = "目录", TaskType = "group", Source = new PuloniaTaskSource
                { Kind = "directory", TaskType = "pathing", Path = Path.Combine(root, "dir"), Recursive = true } }
        ] } };
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(plan, new PuloniaTaskBuildOptions
        {
            Definitions =
            [
                new PuloniaTaskDefinition { TaskType = "javascript", ResourceBaseDirectory = root },
                new PuloniaTaskDefinition { TaskType = "pathing", ResourceBaseDirectory = root }
            ]
        });
        var children = snapshot.RootTask.Children;
        Assert.Equal(JavaScriptVersion, children[0].ResourceVersion);
        Assert.Equal(RouteVersion, children[1].ResourceVersion);
        Assert.Equal(DirectoryVersion, children[2].ResourceVersion);
    }

    /// <summary>Git 与文件式仓库对相同正文得到与本地资源相同的指纹。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RepositoryFingerprints_MatchFixedValues(bool git)
    {
        var source = Path.Combine(_root, "repos", "source");
        Write(source, "repo.json", "{\"indexes\":[]}");
        CreateContent(Path.Combine(source, "repo", "pathing"));
        if (git)
        {
            Repository.Init(source);
            using var repository = new Repository(source);
            Commands.Stage(repository, "*");
            var author = new Signature("fingerprint-test", "fingerprint-test@example.invalid", DateTimeOffset.UtcNow);
            repository.Commit("fingerprint", author, author);
        }
        var store = new ScriptRepositoryStore(Path.Combine(_root, "resources"), Path.Combine(_root, "repos"));
        var registration = await store.RegisterAsync(source);
        using var snapshot = await store.OpenCurrentAsync(registration.Id);
        Assert.Equal(RouteVersion, PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(snapshot, "pathing/route.json", "pathing"));
        Assert.Equal(JavaScriptVersion, PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(snapshot, "pathing/demo", "javascript"));
        Assert.Equal(DirectoryVersion, PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(snapshot, "pathing/dir", "pathing",
            recursive: true, directory: true));
    }

    /// <summary>在指定目录写入固定的路线、JS 项目和目录资源。</summary>
    private static string CreateContent(string root)
    {
        Write(root, "route.json", "{\"route\":1}");
        Write(root, "demo/main.js", "// 测试脚本");
        Write(root, "demo/manifest.json", "{\"name\":\"测试脚本\",\"version\":\"1.0\"}");
        Write(root, "demo/helper.js", "// 不参与 JS 指纹");
        Write(root, "dir/a.json", "{\"a\":1}");
        Write(root, "dir/子目录/b.json", "{\"b\":2}");
        return root;
    }

    /// <summary>写入一个普通 UTF-8 文件。</summary>
    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Utf8);
    }

    /// <summary>清理本测试的临时目录，先解除 Git 对象的只读属性。</summary>
    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}
