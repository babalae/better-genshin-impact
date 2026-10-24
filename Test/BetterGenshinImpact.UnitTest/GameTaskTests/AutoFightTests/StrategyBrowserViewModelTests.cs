using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.ViewModel.Controls.CombatStrategy;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoFightTests;

/// <summary>浏览器的导航、搜索和快照重新定位均不需要 WPF 窗口。</summary>
public sealed class StrategyBrowserViewModelTests
{
    /// <summary>搜索整棵子树，退出后回到之前目录；导航不写入当前配置。</summary>
    [Fact]
    public void NavigationAndSearch_PreserveFolderAndOnlySelectFiles()
    {
        CombatStrategyRef? selected = null;
        var browser = new StrategyBrowserViewModel(CombatStrategyKind.Txt, "文本策略", s => selected = s);
        var deep = Folder("深渊", "深渊", File("队伍.txt", "深渊\\队伍.txt"));
        browser.SetRoot(Folder("文本策略", "", File("日常.txt", "日常.txt"), deep));
        Assert.True(browser.Entries[0].Node.IsFolder);
        browser.Enter(browser.Entries[0]);
        Assert.Null(selected);
        Assert.Equal("深渊", browser.FolderRelativePath);
        browser.ApplySearch("日常");
        Assert.True(browser.IsSearching);
        Assert.Equal("日常.txt", Assert.Single(browser.Entries).Node.Name);
        browser.ApplySearch("");
        Assert.Equal("深渊", browser.FolderRelativePath);
        browser.Enter(Assert.Single(browser.Entries));
        Assert.NotNull(selected);
        browser.GoUp();
        Assert.Equal("", browser.FolderRelativePath);
    }

    /// <summary>刷新时按相对路径保留目录，已删除目录回退到根。</summary>
    [Fact]
    public void SetRoot_RelocatesByRelativePathAndFallsBackWhenMissing()
    {
        var browser = new StrategyBrowserViewModel(CombatStrategyKind.Json, "JSON 策略", _ => { });
        var first = Folder("根", "", Folder("子目录", "子目录", File("A", "子目录\\A.json")));
        browser.SetRoot(first);
        browser.Enter(Assert.Single(browser.Entries));
        var updated = Folder("根", "", Folder("子目录", "子目录", File("B", "子目录\\B.json")));
        browser.SetRoot(updated);
        Assert.Equal("子目录", browser.FolderRelativePath);
        Assert.Equal("B", Assert.Single(browser.Entries).Node.Name);
        browser.SetRoot(Folder("根", "", File("C", "C.json")));
        Assert.Equal("", browser.FolderRelativePath);
        Assert.Equal("C", Assert.Single(browser.Entries).Node.Name);
    }

    /// <summary>按修改时间排序，搜索同时匹配目录名且忽略大小写，错误筛选使用已有元数据。</summary>
    [Fact]
    public void SearchSortAndErrorFilter_UseSnapshotOnly()
    {
        var old = File("旧策略", "深渊\\old.txt");
        var recent = File("新策略", "深渊\\NEW.txt", DateTime.UtcNow);
        var browser = new StrategyBrowserViewModel(CombatStrategyKind.Txt, "文本策略", _ => { });
        browser.SetRoot(Folder("根", "", Folder("深渊", "深渊", old, recent)));
        browser.ApplySearch("深渊");
        Assert.Equal(2, browser.MatchCount);
        browser.SortIndex = 1;
        Assert.Equal(recent, browser.Entries[0].Node);
        browser.ApplySearch("new");
        Assert.Equal(recent, Assert.Single(browser.Entries).Node);
        browser.ApplySearch("");
        browser.ApplyMetadata(new Dictionary<string, CombatStrategyMetadata> { [old.Strategy!.Path] = new("损坏", "损坏") });
        browser.ShowErrorsOnly();
        Assert.Equal(old, Assert.Single(browser.Entries).Node);
        browser.ApplySearch("");
        Assert.False(browser.IsSearching);
    }

    /// <summary>错误筛选开启后到达的新元数据必须更新列表，同路径文件修改后丢弃旧错误。</summary>
    [Fact]
    public void ErrorFilter_UpdatesWithMetadataAndInvalidatesChangedFiles()
    {
        var file = File("策略", "策略.txt");
        var browser = new StrategyBrowserViewModel(CombatStrategyKind.Txt, "文本策略", _ => { });
        browser.SetRoot(Folder("根", "", file));
        browser.ShowErrorsOnly();
        Assert.Empty(browser.Entries);
        browser.ApplyMetadata(new Dictionary<string, CombatStrategyMetadata> { [file.Strategy!.Path] = new("解析失败", "损坏") });
        Assert.Equal(file, Assert.Single(browser.Entries).Node);
        browser.ApplyMetadata(new Dictionary<string, CombatStrategyMetadata> { [file.Strategy.Path] = new("钟离", null) });
        Assert.Empty(browser.Entries);
        browser.ApplyMetadata(new Dictionary<string, CombatStrategyMetadata> { [file.Strategy.Path] = new("解析失败", "损坏") });
        browser.SetRoot(Folder("根", "", File("策略", "策略.txt", DateTime.UtcNow)));
        Assert.Empty(browser.Entries);
        browser.ApplySearch("");
        Assert.False(Assert.Single(browser.Entries).HasError);
    }

    /// <summary>构造不访问磁盘的目录节点。</summary>
    private static CombatStrategyNode Folder(string name, string relative, params CombatStrategyNode[] children) => new()
    {
        Name = name, RelativePath = relative, IsFolder = true, Children = children, FileCount = children.Sum(n => n.FileCount),
    };
    /// <summary>构造不访问磁盘的策略节点。</summary>
    private static CombatStrategyNode File(string name, string relative, DateTime modified = default) => new()
    {
        Name = name, RelativePath = relative, FileCount = 1, LastWriteTimeUtc = modified,
        Strategy = new(CombatStrategyKind.Txt, "C:\\测试\\" + relative, relative, name, true),
    };
}
