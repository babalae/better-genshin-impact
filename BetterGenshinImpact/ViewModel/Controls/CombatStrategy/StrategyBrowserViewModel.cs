using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Controls.CombatStrategy;

/// <summary>手动策略浏览器；搜索与目录导航仅操作内存快照，不依赖 WPF。</summary>
public partial class StrategyBrowserViewModel : StrategyCategoryViewModel
{
    /// <summary>把实际选择交回根 VM。</summary>
    private readonly Action<CombatStrategyRef> _select;
    /// <summary>已加载的元数据。</summary>
    private readonly Dictionary<string, CombatStrategyMetadata> _metadata = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>当前目录节点，搜索时仍保留以便退出搜索后恢复。</summary>
    private CombatStrategyNode? _folder;
    /// <summary>本批元数据取消源。</summary>
    private CancellationTokenSource? _metadataCts;
    /// <summary>当前共享搜索文本。</summary>
    private string _searchText = "";
    /// <summary>是否只显示解析失败项。</summary>
    private bool _errorsOnly;
    /// <summary>当前选中策略路径。</summary>
    private string? _currentPath;
    /// <summary>仅当前可见类别加载元数据。</summary>
    private bool _isActive;
    /// <summary>目录快照的根节点。</summary>
    public CombatStrategyNode? Root { get; private set; }
    /// <summary>列表条目，使用虚拟化容器呈现。</summary>
    public ObservableCollection<StrategyEntryViewModel> Entries { get; } = [];
    /// <summary>面包屑的节点路径。</summary>
    public ObservableCollection<CombatStrategyNode> Breadcrumb { get; } = [];
    /// <summary>可选排序方式。</summary>
    public string[] SortOptions { get; } = ["名称", "最近修改", "创建时间"];
    /// <summary>排序索引，键鼠默认按创建时间倒序。</summary>
    [ObservableProperty] private int _sortIndex;
    /// <summary>列表键盘当前定位的行。</summary>
    [ObservableProperty] private StrategyEntryViewModel? _selectedEntry;
    /// <summary>是否处于搜索或错误筛选。</summary>
    public bool IsSearching => _searchText.Length > 0 || _errorsOnly;
    /// <summary>搜索结果或当前文件夹说明。</summary>
    public string LocationText => IsSearching ? $"{(_errorsOnly ? "解析失败" : "搜索结果")} · {Entries.Count} 项" : Title;
    /// <summary>是否显示空状态。</summary>
    public bool IsEmpty => Entries.Count == 0;
    /// <summary>空状态说明。</summary>
    public string EmptyText => IsSearching ? "没有找到匹配的策略，可切换左侧其他类别查看。"
        : Kind == CombatStrategyKind.KeyMouse ? "还没有录制，可点击下方“录制新的”。" : "还没有策略。把文件放进目录，或从脚本仓库订阅。";
    /// <summary>当前目录相对路径，供底栏打开目录。</summary>
    public string FolderRelativePath => _folder?.RelativePath ?? "";
    /// <summary>元数据或后台读取失败提示。</summary>
    [ObservableProperty] private string _loadError = "";

    /// <summary>创建浏览器，选择行为由根 VM 统一处理。</summary>
    public StrategyBrowserViewModel(CombatStrategyKind kind, string title, Action<CombatStrategyRef> select) : base(kind, title)
    {
        _select = select;
        _sortIndex = kind == CombatStrategyKind.KeyMouse ? 2 : 0;
    }

    /// <summary>替换快照并重新定位当前目录，目录已移除时退回根目录。</summary>
    public void SetRoot(CombatStrategyNode root, string? locatePath = null)
    {
        var relative = FolderRelativePath;
        // 同路径文件修改后不能沿用旧的解析错误；有效缓存由元数据服务按修改时间管理。
        var currentFiles = CombatStrategyCatalog.Flatten(root).ToDictionary(n => n.Strategy!.Path, StringComparer.OrdinalIgnoreCase);
        var previousFiles = Root == null ? [] : CombatStrategyCatalog.Flatten(Root).ToDictionary(n => n.Strategy!.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var path in _metadata.Keys.ToArray())
            if (!currentFiles.TryGetValue(path, out var current) || !previousFiles.TryGetValue(path, out var previous)
                || current.LastWriteTimeUtc != previous.LastWriteTimeUtc) _metadata.Remove(path);
        Root = root;
        Count = root.FileCount;
        // 定位文件时通过树查找父节点，避免依赖具体磁盘根目录。
        _folder = locatePath == null ? FindFolder(root, relative ?? "") ?? root : FindParent(root, locatePath) ?? root;
        Rebuild();
    }

    /// <summary>当前值变化时只更新高亮，不写回宿主绑定。</summary>
    public void SetCurrent(string? path)
    {
        _currentPath = path;
        foreach (var entry in Entries) entry.IsCurrent = path != null && entry.Node.Strategy is { } strategy
            && string.Equals(strategy.Path, path, StringComparison.OrdinalIgnoreCase);
        SelectedEntry = Entries.FirstOrDefault(e => e.IsCurrent);
    }

    /// <summary>进入文件夹或选择策略，一级类别切换不会调用此方法。</summary>
    [RelayCommand]
    public void Enter(StrategyEntryViewModel? entry)
    {
        if (entry == null) return;
        if (entry.Node.IsFolder) { _folder = entry.Node; Rebuild(); }
        else if (entry.Node.Strategy is { } strategy) _select(strategy);
    }

    /// <summary>回到上一级目录；搜索期间保持搜索前目录。</summary>
    [RelayCommand]
    public void GoUp()
    {
        if (Root == null || _folder == null || IsSearching) return;
        _folder = FindFolder(Root, Path.GetDirectoryName(_folder.RelativePath) ?? "") ?? Root;
        Rebuild();
    }

    /// <summary>跳到面包屑对应文件夹。</summary>
    [RelayCommand]
    public void Navigate(CombatStrategyNode? node)
    {
        if (node == null) return;
        _folder = node;
        Rebuild();
    }

    /// <summary>对整棵类别树应用搜索，清空时恢复此前目录。</summary>
    public void ApplySearch(string text)
    {
        _searchText = text;
        if (text.Length == 0) _errorsOnly = false;
        Rebuild();
    }

    /// <summary>显示已加载范围中的解析失败项。</summary>
    public void ShowErrorsOnly()
    {
        _errorsOnly = true;
        Rebuild();
    }

    /// <summary>接收自动匹配页预加载的元数据。</summary>
    public void ApplyMetadata(IReadOnlyDictionary<string, CombatStrategyMetadata> metadata)
    {
        foreach (var pair in metadata) _metadata[pair.Key] = pair.Value;
        foreach (var entry in Entries) FillMetadata(entry);
        // 新元数据可能增加或移除错误项，错误筛选必须重新构建，但不能再次请求同一批。
        if (_errorsOnly) Rebuild(requestMetadata: false);
    }

    /// <summary>关闭和卸载时结束当前批次，不丢弃目录快照。</summary>
    public override void CancelLoading()
    {
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        _metadataCts = null;
    }

    /// <summary>切换类别时取消隐藏列表的读取，只加载可见列表。</summary>
    public void SetActive(bool active)
    {
        _isActive = active;
        if (active) Rebuild(); else CancelLoading();
    }

    /// <summary>列表滚动后只请求可见批次，避免 200 行以后的策略一直没有副标题。</summary>
    public void RequestMetadata(int start, int count)
    {
        if (!_isActive) return;
        var files = Entries.Skip(Math.Max(0, start)).Take(Math.Clamp(count, 1, 200))
            .Where(e => !e.Node.IsFolder).Select(e => e.Node).ToArray();
        CancelLoading();
        _metadataCts = new();
        _ = LoadMetadataAsync(files, _metadataCts.Token);
    }

    /// <summary>排序变化后仅调整当前内存列表。</summary>
    partial void OnSortIndexChanged(int value) => Rebuild();

    /// <summary>从快照生成当前列表并启动受限的元数据读取。</summary>
    private void Rebuild(bool requestMetadata = true)
    {
        Entries.Clear();
        Breadcrumb.Clear();
        if (Root == null)
        {
            MatchCount = IsSearching ? 0 : null;
            NotifyLocation();
            return;
        }
        var files = CombatStrategyCatalog.Flatten(Root);
        IEnumerable<CombatStrategyNode> nodes = IsSearching
            ? files.Where(n => (_searchText.Length == 0 || n.RelativePath.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                              && (!_errorsOnly || _metadata.TryGetValue(n.Strategy!.Path, out var m) && m.Error != null))
            : (_folder ?? Root).Children;
        var sorted = nodes.OrderByDescending(n => n.IsFolder);
        nodes = SortIndex switch
        {
            1 => sorted.ThenByDescending(n => n.LastWriteTimeUtc).ThenBy(n => n.Name),
            2 => sorted.ThenByDescending(n => n.CreationTimeUtc).ThenBy(n => n.Name),
            _ => sorted.ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        foreach (var node in nodes)
        {
            var entry = new StrategyEntryViewModel(node)
            {
                IsCurrent = _currentPath != null && node.Strategy is { } strategy
                    && string.Equals(strategy.Path, _currentPath, StringComparison.OrdinalIgnoreCase),
            };
            FillMetadata(entry);
            Entries.Add(entry);
        }
        MatchCount = IsSearching ? Entries.Count : null;
        SelectedEntry = Entries.FirstOrDefault(e => e.IsCurrent);
        if (!IsSearching)
        {
            Breadcrumb.Add(Root);
            var path = "";
            foreach (var segment in (_folder?.RelativePath ?? "").Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                path = Path.Combine(path, segment);
                if (FindFolder(Root, path) is { } node) Breadcrumb.Add(node);
            }
        }
        NotifyLocation();
        CancelLoading();
        if (requestMetadata) RequestMetadata(SelectedEntry == null ? 0 : Math.Max(0, Entries.IndexOf(SelectedEntry) - 10), 200);
    }

    /// <summary>目录、搜索和空状态一起刷新，避免尚无快照时残留旧状态。</summary>
    private void NotifyLocation()
    {
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(LocationText));
        OnPropertyChanged(nameof(FolderRelativePath));
    }

    /// <summary>后台读取当前列表前 200 个策略；取消和读取错误都在本层处理。</summary>
    private async Task LoadMetadataAsync(IReadOnlyList<CombatStrategyNode> files, CancellationToken ct)
    {
        try
        {
            LoadError = "";
            var result = await CombatStrategyMetadataProvider.GetAsync(files, ct);
            ct.ThrowIfCancellationRequested();
            ApplyMetadata(result);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) LoadError = "读取策略信息失败：" + ex.Message; }
    }

    /// <summary>填充行副标题，搜索结果优先显示相对路径。</summary>
    private void FillMetadata(StrategyEntryViewModel entry)
    {
        if (entry.Node.Strategy is not { } strategy) return;
        if (_metadata.TryGetValue(strategy.Path, out var metadata))
        {
            entry.HasError = metadata.Error != null;
            entry.Subtitle = IsSearching && !_errorsOnly ? entry.Node.RelativePath : metadata.Summary;
        }
        else entry.Subtitle = IsSearching ? entry.Node.RelativePath : "正在读取策略信息…";
    }

    /// <summary>在树中按相对路径查找文件夹。</summary>
    private static CombatStrategyNode? FindFolder(CombatStrategyNode node, string relative)
        => node.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase) ? node
            : node.Children.Where(n => n.IsFolder).Select(n => FindFolder(n, relative)).FirstOrDefault(n => n != null);

    /// <summary>按文件绝对路径查找其父文件夹。</summary>
    private static CombatStrategyNode? FindParent(CombatStrategyNode node, string path)
        => node.Children.Any(n => string.Equals(n.Strategy?.Path, path, StringComparison.OrdinalIgnoreCase)) ? node
            : node.Children.Where(n => n.IsFolder).Select(n => FindParent(n, path)).FirstOrDefault(n => n != null);
}
