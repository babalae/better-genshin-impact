using BetterGenshinImpact.GameTask.AutoFight.Strategy;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

/// <summary>验证角色摘要、错误隔离、宏时长、大小限制与缓存失效。</summary>
[Collection("CombatStrategyFiles")]
public sealed class CombatStrategyMetadataProviderTests : IDisposable
{
    /// <summary>独占策略目录。</summary>
    private readonly StrategyTestFiles _files = new();

    /// <summary>复用现有策略解析器提取文本角色及 JSON 作者、角色。</summary>
    [Fact]
    public async Task GetAsync_ReadsTextAndJsonSummariesAndErrors()
    {
        var txt = Node(_files.Fight("队伍.txt", "钟离 e\n班尼特 q"));
        var json = Node(_files.Fight("条件.json", "{\"info\":{\"author\":\"测试作者\"},\"actions\":[{\"character\":\"那维莱特\",\"action\":\"e\"}]}"));
        var invalid = Node(_files.Fight("损坏.json", "{"));
        var metadata = await CombatStrategyMetadataProvider.GetAsync([txt, json, invalid], default);
        Assert.Null(metadata[txt.Strategy!.Path].Error);
        Assert.Contains("钟离", metadata[txt.Strategy.Path].Summary);
        Assert.Contains("班尼特", metadata[txt.Strategy.Path].Summary);
        Assert.Contains("作者 测试作者", metadata[json.Strategy!.Path].Summary);
        Assert.Contains("那维莱特", metadata[json.Strategy.Path].Summary);
        Assert.NotNull(metadata[invalid.Strategy!.Path].Error);
    }

    /// <summary>只统计 macroEvents 的直接事件及其 time，不把其他字段计入时长。</summary>
    [Fact]
    public async Task GetAsync_ReadsMacroDurationAndInvalidatesCache()
    {
        var path = _files.Macro("宏.json", "{\"info\":{\"time\":999999},\"macroEvents\":[{\"time\":0},{\"time\":83000}]}");
        var node = Node(path);
        var first = (await CombatStrategyMetadataProvider.GetAsync([node], default))[path];
        Assert.Equal("时长 1:23 · 2 个事件", first.Summary);
        var cached = (await CombatStrategyMetadataProvider.GetAsync([node], default))[path];
        Assert.Same(first, cached);
        File.WriteAllText(path, "{\"macroEvents\":[{\"time\":2000}]}");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        var updated = (await CombatStrategyMetadataProvider.GetAsync([Node(path)], default))[path];
        Assert.Equal("时长 0:02 · 1 个事件", updated.Summary);
        Assert.NotSame(first, updated);
    }

    /// <summary>超过 5 MB 的录制只显示大小，不解析内容。</summary>
    [Fact]
    public async Task GetAsync_LargeMacro_DoesNotParseContent()
    {
        var path = _files.Macro("大文件.json", new string('x', 5 * 1024 * 1024 + 1));
        var metadata = (await CombatStrategyMetadataProvider.GetAsync([Node(path)], default))[path];
        Assert.Null(metadata.Error);
        Assert.Contains("文件大小", metadata.Summary);
    }

    /// <summary>建立带修改时间的文件节点。</summary>
    private static CombatStrategyNode Node(string path) => new()
    {
        Name = Path.GetFileNameWithoutExtension(path), RelativePath = Path.GetFileName(path),
        Strategy = CombatStrategyResolver.Parse(path), LastWriteTimeUtc = File.GetLastWriteTimeUtc(path), FileCount = 1,
    };

    /// <summary>清理测试文件。</summary>
    public void Dispose() => _files.Dispose();
}
