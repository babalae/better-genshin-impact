using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.Service.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>只读快照浏览；按日期懒加载，按物品遍历日期，共用窗口内缓存。</summary>
public partial class AccountRecordsSnapshotsViewModel : ObservableObject
{
    /// <summary>快照存储。</summary>
    private readonly InventoryStore _store;
    /// <summary>库存图标与名称转换。</summary>
    private readonly AccountRecordsInventoryRowFactory _factory;
    /// <summary>当前窗口绑定的账号。</summary>
    private readonly string _uid;
    /// <summary>已开始读取的快照，共用进行中的请求，关闭窗口后释放。</summary>
    private readonly Dictionary<string, Task<InventoryWorkspace>> _cache = [];
    /// <summary>请求序号，快速切换和关闭时丢弃旧结果。</summary>
    private int _generation;
    /// <summary>窗口关闭后禁止继续加载其他日期。</summary>
    private bool _closed;
    /// <summary>日期列表，最新日期在前。</summary>
    public ObservableCollection<string> Dates { get; } = [];
    /// <summary>日期总表的原始行。</summary>
    private readonly List<AccountRecordsInventoryRow> _rows = [];
    /// <summary>可筛选和排序的日期总表。</summary>
    public ListCollectionView Items { get; }
    /// <summary>物品选择目录。</summary>
    private readonly List<SnapshotCatalogItem> _catalog = [];
    /// <summary>按分类分组的物品列表。</summary>
    public ListCollectionView Catalog { get; }
    /// <summary>总表类型筛选选项，空字符串表示全部。</summary>
    public ObservableCollection<string> Categories { get; } = [""];
    /// <summary>当前模式：日期或物品。</summary>
    [ObservableProperty] private int _mode;
    /// <summary>当前选择的日期。</summary>
    [ObservableProperty] private string? _selectedDate;
    /// <summary>当前选择的物品；未选择时不读取所有日期。</summary>
    [ObservableProperty] private SnapshotCatalogItem? _selectedItem;
    /// <summary>搜索名称或分类。</summary>
    [ObservableProperty] private string _searchText = "";
    /// <summary>总表分类过滤条件。</summary>
    [ObservableProperty] private string _category = "";
    /// <summary>当前所选物品的各日期记录。</summary>
    [ObservableProperty] private IReadOnlyList<SnapshotItemHistory> _history = Array.Empty<SnapshotItemHistory>();
    /// <summary>加载提示及失败原因。</summary>
    [ObservableProperty] private string _status = "";
    /// <summary>缺图行号遵循库存偏好。</summary>
    [ObservableProperty] private bool _showMissingIconRowNumbers;
    /// <summary>当前日期表匹配数量。</summary>
    public string CountText => string.Format(I18nService.Instance.Translate("共 {0} 项，筛选后 {1} 项"), _rows.Count, Items.Cast<object>().Count());

    /// <summary>构建只读集合，不访问文件；调用 InitializeAsync 后加载默认日期。</summary>
    public AccountRecordsSnapshotsViewModel(InventoryStore store, InventoryCatalog catalog, InventoryIconCache icons, string uid)
    {
        _store = store; _uid = uid; _factory = new(catalog, icons);
        Items = new(_rows) { Culture = catalog.TextSortCulture };
        Items.SortDescriptions.Add(new(nameof(AccountRecordsInventoryRow.RecordedAt), ListSortDirection.Descending));
        Items.SortDescriptions.Add(new(nameof(AccountRecordsInventoryRow.ItemNameSortKey), ListSortDirection.Ascending));
        Items.Filter = value => value is AccountRecordsInventoryRow row &&
            (string.IsNullOrEmpty(Category) || row.Category == Category) && Matches(row.Name, row.Category);
        ((INotifyCollectionChanged)Items).CollectionChanged += (_, _) => UpdateCounts();
        Catalog = new(_catalog) { Culture = catalog.TextSortCulture };
        Catalog.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SnapshotCatalogItem.Category)));
        Catalog.Filter = value => value is SnapshotCatalogItem item && Matches(item.Name, item.Category);
    }

    /// <summary>不区分大小写的名称或类型搜索。</summary>
    private bool Matches(string name, string category) => name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
        category.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    /// <summary>行号跟随实际可见顺序，切换排序与筛选后仍与库存表一致。</summary>
    private void UpdateCounts()
    {
        var number = 0;
        foreach (var row in Items.Cast<AccountRecordsInventoryRow>()) row.DisplayRowNumber = $"{++number}.";
        OnPropertyChanged(nameof(CountText));
    }
    /// <summary>更新两个模式的筛选。</summary>
    partial void OnSearchTextChanged(string value) { Items.Refresh(); Catalog.Refresh(); }
    /// <summary>仅调整总表分类筛选。</summary>
    partial void OnCategoryChanged(string value) => Items.Refresh();
    /// <summary>切换模式时只加载该模式所选目标。</summary>
    partial void OnModeChanged(int value) => _ = LoadSelectionAsync();
    /// <summary>选中日期才读取该文件。</summary>
    partial void OnSelectedDateChanged(string? value) { if (Mode == 0) _ = LoadSelectionAsync(); }
    /// <summary>选中物品才扫描可用快照。</summary>
    partial void OnSelectedItemChanged(SnapshotCatalogItem? value) { if (Mode == 1) _ = LoadSelectionAsync(); }

    /// <summary>枚举日期及现有字典，优先选择上一记录日，否则选择此前最近的日期。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var dates = await _store.GetSnapshotDatesAsync(_uid);
            var classifications = await _store.GetItemClassificationsAsync();
            if (_closed) return;
            foreach (var entry in classifications.Categories.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key))
                _catalog.Add(new(entry.Key, entry.Value));
            Catalog.Refresh();
            foreach (var date in dates) Dates.Add(date);
            var today = InventoryStore.RecordDay(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd");
            _selectedDate = dates.FirstOrDefault(date => string.CompareOrdinal(date, today) < 0) ?? dates.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedDate));
            if (SelectedDate == null) Status = I18nService.Instance.Translate("此账号暂无快照。");
            else await LoadSelectionAsync();
        }
        catch (Exception ex) { if (!_closed) Status = AccountRecordsErrorMessages.Describe(ex); }
    }

    /// <summary>缓存文件解析结果；读取失败不永久缓存，以便再次选择时重试。</summary>
    private async Task<InventoryWorkspace> ReadAsync(string date)
    {
        if (!_cache.TryGetValue(date, out var task)) _cache[date] = task = _store.ReadSnapshotAsync(_uid, date);
        try { return await task; }
        catch { _cache.Remove(date); throw; }
    }

    /// <summary>后台读取后一次替换 UI 集合，不把部分历史误作完整结果。</summary>
    public async Task LoadSelectionAsync()
    {
        var generation = ++_generation;
        if (_closed) return;
        Status = I18nService.Instance.Translate("正在读取快照…");
        try
        {
            if (Mode == 0 && SelectedDate is { } date)
            {
                _rows.Clear(); Items.Refresh();
                var workspace = await ReadAsync(date);
                var rows = await _factory.BuildAsync(workspace);
                if (_closed || generation != _generation) return;
                ShowMissingIconRowNumbers = workspace.Settings.ShowMissingIconRowNumbers;
                Categories.Clear(); Categories.Add("");
                foreach (var type in rows.Select(row => row.Category).Distinct().OrderBy(value => value)) Categories.Add(type);
                Category = "";
                _rows.AddRange(rows);
                // 已打开日期中的历史自定义物品也可进入查询目录，不预读其余文件。
                var knownNames = _catalog.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
                foreach (var row in rows.Where(row => knownNames.Add(row.Name)))
                    _catalog.Add(new(row.Name, row.Category));
                Catalog.Refresh(); Items.Refresh();
                Status = string.Format(I18nService.Instance.Translate("历史快照 · {0} · UID {1} · 只读确认库存，未还原状态灯"), date, _uid);
                if (workspace.BackupWarnings.Length > 0) Status += "\n" + string.Join("\n", workspace.BackupWarnings.Select(message => I18nService.Instance.Translate(message)));
            }
            else if (Mode == 1 && SelectedItem is { } item)
            {
                History = Array.Empty<SnapshotItemHistory>();
                var history = new List<SnapshotItemHistory>();
                foreach (var day in Dates)
                {
                    if (_closed || generation != _generation) return;
                    try
                    {
                        var workspace = await ReadAsync(day);
                        var record = workspace.Inventory.Items.GetValueOrDefault(item.Name)?.FirstOrDefault();
                        var reference = record?.Source ?? "";
                        history.Add(new(day, record?.Quantity, record?.RecordedAt,
                            workspace.Settings.PreferReadableSource && workspace.Sources.TryGetValue(reference, out var source) &&
                                !string.IsNullOrWhiteSpace(source.DisplayName) ? source.DisplayName : reference, ""));
                    }
                    catch (Exception ex) { history.Add(new(day, null, null, "", AccountRecordsErrorMessages.Describe(ex))); }
                }
                if (_closed || generation != _generation) return;
                History = history;
                Status = string.Format(I18nService.Instance.Translate("{0} · 共 {1} 份快照；无记录不等于数量为零。"), item.Name, history.Count);
            }
            else Status = I18nService.Instance.Translate("请选择日期或物品查看快照。");
        }
        catch (Exception ex) { if (!_closed && generation == _generation) Status = AccountRecordsErrorMessages.Describe(ex); }
    }

    /// <summary>关闭后阻止旧请求更新窗口或继续遍历日期。</summary>
    public void Close() { _closed = true; _generation++; _cache.Clear(); }
}

/// <summary>字典式选择列表，仅保存名称和分类。</summary>
/// <param name="Name">标准物品名称。</param>
/// <param name="Category">物品类型。</param>
public sealed record SnapshotCatalogItem(string Name, string Category);

/// <summary>一个物品在某日快照中的确认数量，未知与零分开。</summary>
/// <param name="Date">快照日期。</param>
/// <param name="Quantity">确认数量。</param>
/// <param name="RecordedAt">原记录时间。</param>
/// <param name="Source">来源名称。</param>
/// <param name="Error">文件读取失败信息。</param>
public sealed record SnapshotItemHistory(string Date, long? Quantity, DateTimeOffset? RecordedAt, string Source, string Error)
{
    /// <summary>缺失与失败均不伪装成零数量。</summary>
    public string QuantityText => Error.Length > 0 ? I18nService.Instance.Translate("读取失败") : Quantity?.ToString() ?? I18nService.Instance.Translate("无记录");
    /// <summary>保留库存记录的真实时间。</summary>
    public string TimeText => RecordedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "";
}
