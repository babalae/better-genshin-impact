using BetterGenshinImpact.GameTask.AutoFight.Strategy;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

/// <summary>验证目录树、剪枝、规范值、类型限制与自动匹配范围。</summary>
[Collection("CombatStrategyFiles")]
public sealed class CombatStrategyCatalogTests : IDisposable
{
    /// <summary>独占策略目录。</summary>
    private readonly StrategyTestFiles _files = new();

    /// <summary>每种策略单独成树，保留多级目录并剪除不含策略的目录。</summary>
    [Fact]
    public async Task LoadAsync_BuildsTypedTreesAndTextOnlyScopes()
    {
        var text = _files.Fight("深渊\\12层\\同名.txt");
        _files.Fight("深渊\\12层\\同名.json", "{}");
        _files.Fight("仅JSON\\条件.json", "{}");
        _files.Fight("空目录\\说明.md", "说明");
        var macro = _files.Macro("AutoFight\\录制.json");
        var snapshot = await CombatStrategyCatalog.LoadAsync(CombatStrategyKind.All, default);
        var root = snapshot.Roots[CombatStrategyKind.Txt].Children.Single(n => n.Name == _files.Prefix);
        Assert.Equal(1, root.FileCount);
        Assert.Equal("深渊", Assert.Single(root.Children).Name);
        var node = Assert.Single(CombatStrategyCatalog.Flatten(root));
        Assert.Equal(text, node.Strategy!.Path);
        Assert.EndsWith("同名.txt", node.Strategy.Value);
        Assert.Contains(snapshot.AutoMatchScopes, n => n.RelativePath == Path.Combine(_files.Prefix, "深渊", "12层"));
        Assert.DoesNotContain(snapshot.AutoMatchScopes, n => n.RelativePath == Path.Combine(_files.Prefix, "仅JSON"));
        Assert.Contains(CombatStrategyCatalog.Flatten(snapshot.Roots[CombatStrategyKind.KeyMouse]), n => n.Strategy!.Path == macro);
    }

    /// <summary>只允许自动匹配时仍可扫描 txt 范围，不暴露手动根节点。</summary>
    [Fact]
    public async Task LoadAsync_AutoOnly_HidesManualRoots()
    {
        _files.Fight("范围\\队伍.txt");
        var snapshot = await CombatStrategyCatalog.LoadAsync(CombatStrategyKind.AutoSelect, default);
        Assert.Empty(snapshot.Roots);
        Assert.Contains(snapshot.AutoMatchScopes, n => n.RelativePath == Path.Combine(_files.Prefix, "范围"));
    }

    /// <summary>预先取消时不继续扫描。</summary>
    [Fact]
    public async Task LoadAsync_ObservesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CombatStrategyCatalog.LoadAsync(CombatStrategyKind.All, cts.Token));
    }

    /// <summary>清理测试文件。</summary>
    public void Dispose() => _files.Dispose();
}
