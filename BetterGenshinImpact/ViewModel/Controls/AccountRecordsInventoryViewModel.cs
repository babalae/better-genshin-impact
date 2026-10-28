using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using BetterGenshinImpact.Core.Config;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using System.Diagnostics;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>库存分页的筛选、编辑、异常处理和保存设置。</summary>
public partial class AccountRecordsInventoryViewModel : ObservableObject
{
    /// <summary>集中生成展示快照，刷新流程只负责应用结果。</summary>
    private readonly AccountRecordsInventoryRowFactory _rowFactory;
    /// <summary>只有完整加载成功的当前账号快照才可在返回页面时直接复用。</summary>
    private bool _hasSnapshot;
    /// <summary>唯一库存写入入口。</summary>
    private readonly InventoryStore _store;
    /// <summary>公共名称字典。</summary>
    private readonly InventoryCatalog _catalog;
    /// <summary>跨账号图标缓存。</summary>
    private readonly InventoryIconCache _icons;
    /// <summary>最近一次完整读取结果。</summary>
    private InventoryWorkspace _workspace = new();
    /// <summary>异步切换序号，避免旧账号结果覆盖新账号。</summary>
    private int _loadVersion;
    /// <summary>当前账号。</summary>
    public string? Uid { get; private set; }
    /// <summary>显示行集合。</summary>
    public AccountRecordsInventoryRows Rows { get; } = [];
    /// <summary>上次显示分隔线的行；更新时只修改旧、新边界。</summary>
    private AccountRecordsInventoryRow? _priorBoundary;
    /// <summary>页面排序和筛选视图。</summary>
    public ICollectionView Items { get; }
    /// <summary>是否没有匹配行。</summary>
    public bool IsEmpty => !IsLoading && Items.IsEmpty;
    /// <summary>大表加载期间仅隐藏表体，账号、页签和其他页面操作继续可用。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    private bool _isLoading;
    /// <summary>加载或没有匹配行时展示水印。</summary>
    public bool ShowPlaceholder => IsLoading || IsEmpty;
    /// <summary>库存与后续子表共用的图像缓存显示偏好。</summary>
    public bool UseImageCache => _workspace.Settings.UseImageCache;

    /// <summary>切换后重建行模型以释放旧图片引用，不删除磁盘文件。</summary>
    [RelayCommand]
    private Task ToggleImageCache() => RunDisplayAsync(async () =>
    {
        _workspace.Settings.UseImageCache = !UseImageCache;
        await _store.ConfigureAsync(_workspace.Settings);
        OnPropertyChanged(nameof(UseImageCache));
    });
    /// <summary>无图标行的占位显示偏好。</summary>
    public bool ShowMissingIconRowNumbers => _workspace.Settings.ShowMissingIconRowNumbers;
    /// <summary>用户主动选择的过期时间列显示偏好。</summary>
    public bool ShowExpirationColumn => _workspace.Settings.ShowExpirationColumn;

    /// <summary>保存表头列选择，不根据是否有到期数据自动改变。</summary>
    [RelayCommand]
    private Task ToggleExpirationColumn() => RunDisplayAsync(async () =>
    {
        _workspace.Settings.ShowExpirationColumn = !ShowExpirationColumn;
        await _store.ConfigureAsync(_workspace.Settings);
        OnPropertyChanged(nameof(ShowExpirationColumn));
    });

    /// <summary>保存跨账号共享的行号占位开关。</summary>
    [RelayCommand]
    private Task ToggleMissingIconRowNumbers() => RunDisplayAsync(async () =>
    {
        _workspace.Settings.ShowMissingIconRowNumbers = !ShowMissingIconRowNumbers;
        await _store.ConfigureAsync(_workspace.Settings);
        OnPropertyChanged(nameof(ShowMissingIconRowNumbers));
    });
    /// <summary>当前账号的总项数与搜索、分类筛选后可见的项数。</summary>
    public string ItemCountText => $"共 {Rows.Count} 项，筛选后 {Items.Cast<AccountRecordsInventoryRow>().Count()} 项";
    /// <summary>是否选择了账号。</summary>
    public bool HasUid => Uid != null;
    /// <summary>默认显示来源名称。</summary>
    public bool PreferReadableSource => _workspace.Settings.PreferReadableSource;
    /// <summary>快照开关状态。</summary>
    public bool SnapshotsEnabled => _workspace.Settings.SnapshotsEnabled;
    /// <summary>底部事件说明、导入摘要或操作错误；空文本时隐藏提示区。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(NoticeErrorCountText))]
    private string _status = "";
    /// <summary>是否显示提示区，不控制下方常驻项数区域。</summary>
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    /// <summary>只为当前错误提示显示总数；独立布局保证说明截断时仍可见。</summary>
    public string NoticeErrorCountText => NoticeRow is { ErrorCount: > 1, Event.State: "Error" } row && Status == row.Event.Message
        ? $"（共{row.ErrorCount}个错误）" : "";
    /// <summary>当前底部提示对应行。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBatchArchive))]
    [NotifyPropertyChangedFor(nameof(HasCompletedEvents))]
    [NotifyPropertyChangedFor(nameof(CanResolveNotice))]
    [NotifyPropertyChangedFor(nameof(CanAcceptNotice))]
    [NotifyPropertyChangedFor(nameof(CanAcknowledgeNotice))]
    [NotifyPropertyChangedFor(nameof(NoticeErrorCountText))]
    private AccountRecordsInventoryRow? _noticeRow;
    /// <summary>提示打开时固定账号。</summary>
    private string? _noticeUid;
    /// <summary>点击固定的材料；其他状态灯的悬停不会替换此目标。</summary>
    private AccountRecordsInventoryRow? _selectedNoticeRow;

    /// <summary>只高亮实际占用底部提示的材料行。</summary>
    partial void OnNoticeRowChanged(AccountRecordsInventoryRow? oldValue, AccountRecordsInventoryRow? newValue)
    {
        if (oldValue != null) oldValue.IsNoticeActive = false;
        if (newValue != null) newValue.IsNoticeActive = true;
    }
    /// <summary>两行提示的第二行。</summary>
    [ObservableProperty] private string _noticeDetails = "";
    /// <summary>当前错误允许舍弃。</summary>
    public bool CanResolveNotice => NoticeRow?.Error != null;
    /// <summary>只有普通记录冲突可主动接受，过期核实不能绕过。</summary>
    public bool CanAcceptNotice => NoticeRow?.CanAcceptDisplayedError == true;
    /// <summary>Completed、Warning 及没有待处理库存错误的 Error 事件允许归档。</summary>
    public bool CanAcknowledgeNotice => NoticeRow?.Event != null && (NoticeRow.StateRank is 1 or 3 || NoticeRow.StateRank == 0 && NoticeRow.Error == null);

    /// <summary>打开阈值规则配置，首次创建禁用的示例；保存后刷新或等候分钟检查生效。</summary>
    [RelayCommand]
    private async Task EditWarningRules()
    {
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(async () =>
            {
                var path = await _store.GetWarningRulesPathAsync();
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            });
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(ex.Message, "打开 Warning 规则", MessageBoxButton.OK); }
    }

    /// <summary>当前搜索词。</summary>
    [ObservableProperty] private string _searchText = "";
    /// <summary>完整字典中的分类，包含当前没有库存的分类。</summary>
    public ObservableCollection<AccountRecordsCategoryOption> Categories { get; } = [];
    /// <summary>批量勾选时只刷新一次。</summary>
    private bool _updatingCategories;
    /// <summary>非全选时显示蓝色漏斗。</summary>
    public bool HasCategoryFilter => Categories.Any(category => !category.IsSelected);
    /// <summary>全部类型开关，可一键全选或取消全选。</summary>
    public bool AllCategoriesSelected
    {
        get => !HasCategoryFilter;
        set
        {
            _updatingCategories = true;
            foreach (var category in Categories) category.IsSelected = value;
            _updatingCategories = false;
            CategoriesChanged();
        }
    }

    /// <summary>创建分页视图，不在构造时执行磁盘读取。</summary>
    public AccountRecordsInventoryViewModel(InventoryStore store, InventoryCatalog catalog, InventoryIconCache icons)
    {
        _store = store; _catalog = catalog; _icons = icons;
        _rowFactory = new AccountRecordsInventoryRowFactory(catalog, icons);
        Items = CollectionViewSource.GetDefaultView(Rows);
        if (Items is ListCollectionView list) list.IsLiveSorting = false;
        // 名称、类型及来源等文本列共用字典的中文排序规则，数值和时间列保持原有排序。
        Items.Culture = _catalog.TextSortCulture;
        Items.Filter = value => value is AccountRecordsInventoryRow row &&
            (!HasCategoryFilter || Categories.Any(category => category.Name == row.Category && category.IsSelected)) &&
            (row.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || row.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        Items.SortDescriptions.Add(new SortDescription("SortValues.Prior", ListSortDirection.Descending));
        Items.SortDescriptions.Add(new SortDescription("SortValues.RecordedAt", ListSortDirection.Descending));
        Items.CollectionChanged += (_, _) => UpdatePriorSeparator();
    }

    /// <summary>筛选、排序或重载后，将细线定位到最后一条可见置顶物品下方。</summary>
    private void UpdatePriorSeparator()
    {
        if (IsLoading) return;
        var visible = Items.Cast<AccountRecordsInventoryRow>().ToArray();
        var boundary = visible.Any(row => !row.Prior) ? visible.LastOrDefault(row => row.Prior) : null;
        if (ReferenceEquals(_priorBoundary, boundary)) return;
        if (_priorBoundary != null) _priorBoundary.ShowPriorSeparator = false;
        if (boundary != null) boundary.ShowPriorSeparator = true;
        _priorBoundary = boundary;
    }

    /// <summary>切换当前 UID 的置顶状态，重载后保持现有排序和筛选。</summary>
    [RelayCommand]
    private async Task TogglePrior(AccountRecordsInventoryRow? row)
    {
        if (Uid is not { } uid || row == null) return;
        await RunDisplayAsync(() => _store.SetPriorAsync(uid, row.Name, !row.Prior), AccountRecordsRefreshKind.Prior);
    }

    /// <summary>立即更新关键词筛选。</summary>
    partial void OnSearchTextChanged(string value) => Filter();

    /// <summary>分钟刷新剩余天数及阈值事件，不重建库存行或清除用户固定的提示。</summary>
    [RelayCommand]
    private async Task RefreshTimeWarnings()
    {
        if (IsLoading) return;
        try
        {
            if (Uid is not { } uid) return;
            await _store.MaintainAsync(uid);
            if (Uid != uid) return;
            await ResumeAsync(uid);
            foreach (var row in Rows) row.RefreshTimeAndStatus();
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    /// <summary>统一更新全选状态、漏斗颜色和过滤结果。</summary>
    private void CategoriesChanged()
    {
        if (_updatingCategories) return;
        OnPropertyChanged(nameof(HasCategoryFilter)); OnPropertyChanged(nameof(AllCategoriesSelected));
        Filter();
    }
    /// <summary>同步完整字典分类；原选中分类全部消失时恢复全选，主动取消全选则保持。</summary>
    private void UpdateCategories(System.Collections.Generic.IEnumerable<string> categories)
    {
        var names = categories.Select(value => string.IsNullOrEmpty(value) ? "未分类" : value).Append("未分类").Distinct().OrderBy(value => value, _catalog.TextComparer).ToArray();
        if (names.SequenceEqual(Categories.Select(item => item.Name))) return;
        var previous = Categories.ToDictionary(item => item.Name, item => item.IsSelected);
        var allSelected = AllCategoriesSelected;
        var lostSelection = previous.Any(item => item.Value) && !names.Any(name => previous.TryGetValue(name, out var selected) && selected);
        _updatingCategories = true;
        try
        {
            Categories.Clear();
            foreach (var name in names)
                Categories.Add(new AccountRecordsCategoryOption(name, CategoriesChanged)
                { IsSelected = allSelected || lostSelection || previous.TryGetValue(name, out var selected) && selected });
        }
        finally { _updatingCategories = false; }
        CategoriesChanged();
    }
    /// <summary>重置为全部类型，不清除搜索词。</summary>
    [RelayCommand] private void ResetCategories() => AllCategoriesSelected = true;
    /// <summary>刷新搜索与分类过滤，并通知空状态和可见项数。</summary>
    private void Filter() { Items.Refresh(); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(ShowPlaceholder)); OnPropertyChanged(nameof(ItemCountText)); }

    /// <summary>首次进入时恢复偏好，不要求已有 UID。</summary>
    public async Task LoadPreferencesAsync()
    {
        _workspace.Settings = await _store.GetSettingsAsync();
        OnPropertyChanged(nameof(ShowExpirationColumn));
        OnPropertyChanged(nameof(UseImageCache));
        OnPropertyChanged(nameof(ShowMissingIconRowNumbers));
        var categories = await Task.Run(() => _catalog.Items.Values.ToArray());
        UpdateCategories(categories);
        OnPropertyChanged(nameof(PreferReadableSource)); OnPropertyChanged(nameof(SnapshotsEnabled));
    }

    /// <summary>区分并发的局部刷新，丢弃较早返回的快照。</summary>
    private int _refreshVersion;

    /// <summary>统一通知快照变更影响的显示属性和命令。</summary>
    private void NotifyWorkspaceChanged()
    {
        foreach (var property in new[] { nameof(UseImageCache), nameof(ShowExpirationColumn), nameof(ShowMissingIconRowNumbers),
            nameof(PreferReadableSource), nameof(SnapshotsEnabled), nameof(HasCompletedEvents), nameof(ShowBatchArchive),
            nameof(CanAcceptNotice), nameof(CanResolveNotice), nameof(CanAcknowledgeNotice), nameof(NoticeErrorCountText),
            nameof(IsEmpty), nameof(ShowPlaceholder), nameof(ItemCountText) })
            OnPropertyChanged(property);
        DeleteIconCommand.NotifyCanExecuteChanged();
    }

    /// <summary>原位更新已有行；排序键仅在明确要求重新排序时更新。</summary>
    private async Task RefreshRowsAsync(AccountRecordsRefreshKind refresh = AccountRecordsRefreshKind.Content)
    {
        var uid = Uid;
        if (uid == null || IsLoading) return;
        var version = _loadVersion;
        var refreshVersion = ++_refreshVersion;
        var workspace = await _store.ReadAsync(uid, includeHistory: false);
        if (Uid != uid || version != _loadVersion || refreshVersion != _refreshVersion || IsLoading) return;
        var next = await _rowFactory.BuildAsync(workspace);
        if (Uid != uid || version != _loadVersion || refreshVersion != _refreshVersion || IsLoading) return;
        var existing = Rows.ToDictionary(row => row.Name, StringComparer.Ordinal);
        var names = next.Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        var filterChanged = false;
        _workspace = workspace;
        foreach (var row in next)
        {
            if (existing.TryGetValue(row.Name, out var current))
            {
                filterChanged |= current.Category != row.Category;
                current.ApplySnapshot(row, refresh == AccountRecordsRefreshKind.Icons);
            }
            else Rows.Add(row);
        }
        foreach (var row in existing.Values.Where(row => !names.Contains(row.Name))) Rows.Remove(row);
        if (refresh == AccountRecordsRefreshKind.Prior)
            foreach (var row in Rows) row.SortValues.UpdatePrior(row.Prior);
        UpdateCategories(workspace.Definitions.Values);
        if (filterChanged || refresh == AccountRecordsRefreshKind.Prior) Items.Refresh();
        if (_selectedNoticeRow != null && (!Rows.Contains(_selectedNoticeRow) || !_selectedNoticeRow.HasStatusLight))
            _selectedNoticeRow = null;
        if (NoticeRow != null)
        {
            if (!Rows.Contains(NoticeRow) || !NoticeRow.HasStatusLight) CloseNotice();
            else DisplayNotice(NoticeRow);
        }
        NotifyWorkspaceChanged();
        UpdatePriorSeparator();
    }

    /// <summary>返回页面只检查总表修改时间；未变化则直接复用，不维护、不读取整表、不通知行刷新。</summary>
    public async Task ResumeAsync(string? uid)
    {
        if (IsLoading && uid == Uid) return;
        if (uid != Uid || !_hasSnapshot)
        {
            await SelectUidAsync(uid);
            return;
        }
        if (uid == null) return;
        var version = _loadVersion;
        try
        {
            var updatedAt = await _store.GetInventoryUpdatedAtAsync(uid);
            if (Uid == uid && version == _loadVersion && updatedAt != _workspace.UpdatedAt) await RefreshRowsAsync();
        }
        catch (Exception ex)
        {
            if (Uid == uid && version == _loadVersion) Status = $"库存检查失败：{ex.Message} 当前保留上次读取的内容。";
        }
    }

    /// <summary>切换 UID 或主动刷新时完整加载，并丢弃过期异步读取结果。</summary>
    public async Task SelectUidAsync(string? uid)
    {
        var changedUid = Uid != uid;
        if (changedUid) { Rows.Clear(); _workspace = new InventoryWorkspace(); }
        Uid = uid; OnPropertyChanged(nameof(HasUid));
        OnPropertyChanged(nameof(HasCompletedEvents));
        OnPropertyChanged(nameof(ShowBatchArchive));
        var version = ++_loadVersion;
        _hasSnapshot = false;
        IsLoading = uid != null;
        Status = "";
        NoticeRow = null; NoticeDetails = ""; _noticeUid = null; _selectedNoticeRow = null;
        if (uid == null) { Rows.Clear(); OnPropertyChanged(nameof(ShowPlaceholder)); return; }
        try
        {
            // 先让绑定、布局和输入完成，水印先于磁盘读取和建表出现。
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (version != _loadVersion) return;
            await _store.MaintainAsync(uid);
            if (version != _loadVersion) return;
            var workspace = await _store.ReadAsync(uid, includeHistory: false);
            var rows = await _rowFactory.BuildAsync(workspace);
            if (version != _loadVersion) return;
            _workspace = workspace;
            UpdateCategories(workspace.Definitions.Values);
            // 表体暂时隐藏；分时添加让输入持续得到处理，避免长时间占用 UI 线程。
            Rows.Clear();
            var slice = Stopwatch.StartNew();
            foreach (var row in rows)
            {
                Rows.Add(row);
                if (slice.ElapsedMilliseconds < 4) continue;
                await Dispatcher.Yield(DispatcherPriority.Background);
                if (version != _loadVersion) return;
                slice.Restart();
            }
            _hasSnapshot = true;
            NotifyWorkspaceChanged();
            var errors = Rows.Count(row => row.StateRank == 0);
            Status = errors > 0 ? $"{errors} 项库存异常待处理，请点击状态灯查看。" : "";
            if (workspace.WarningRuleError != null) Status = $"提醒规则无效：{workspace.WarningRuleError}";
            OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(PreferReadableSource)); OnPropertyChanged(nameof(SnapshotsEnabled));
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
                Status = $"库存读取失败：{ex.Message}" + (Rows.Count > 0 ? " 当前保留上次读取的内容，请刷新重试。" : " 请刷新重试。");
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
                UpdatePriorSeparator();
                OnPropertyChanged(nameof(ItemCountText));
            }
        }
    }

    /// <summary>当前提示类型在账号内至少有两条未受阻的完成事件。</summary>
    public bool HasCompletedEvents => Uid != null && NoticeRow?.CanBatchArchive == true;
    /// <summary>仅在绿色提示中展示批量归档，且至少有两个可归档事件。</summary>
    public bool ShowBatchArchive => NoticeRow?.StateRank == 3 && HasCompletedEvents;

    /// <summary>以右键物品或底部提示为类型依据，归档当前账号内未受阻的同类完成事件。</summary>
    [RelayCommand]
    private async Task ArchiveCompletedEvents(AccountRecordsInventoryRow? row)
    {
        row ??= NoticeRow;
        if (Uid is not { } uid || row?.CanBatchArchive != true || row.Event == null || !Rows.Contains(row)) return;
        var kind = row.Event.Kind;
        var ids = InventoryEventPolicy.GetCompletedArchiveCandidates(_workspace.Inventory, kind).Select(item => item.Id).ToArray();
        if (ids.Length < 2) return;
        await RunManualAsync(() => _store.ArchiveCompletedEventsAsync(uid, kind, ids));
    }
    /// <summary>在只读窗口查看字典，补充字典与来源字典可显式开启外部编辑。</summary>
    [RelayCommand]
    private async Task ViewDictionary(string kind)
    {
        var relativePath = kind switch
        {
            "items" => @"Assets\Model\ItemV2\item.csv",
            "user" => @"User\AccountRecords\item-overrides.json",
            "sources" => @"User\AccountRecords\sources.json",
            _ => null
        };
        if (relativePath == null) return;
        var path = Global.Absolute(relativePath);
        var title = kind switch { "items" => "物品字典", "user" => "补充字典", _ => "来源字典" };
        new AccountRecordsDictionaryDialog(new AccountRecordsDictionaryViewModel(title, path, kind != "items", _catalog,
            kind == "user", kind == "user" ? _store.RestorePresetCategoriesAsync : null))
        { Owner = Application.Current.MainWindow }.ShowDialog();
        await SelectUidAsync(Uid);
    }
    /// <summary>手动刷新当前账号。</summary>
    [RelayCommand] private Task Refresh() => SelectUidAsync(Uid);

    /// <summary>显示导入摘要；切换账号、刷新或查看事件后不再覆盖当前提示。</summary>
    public async Task ShowImportSummaryAsync(string summary)
    {
        var version = _loadVersion;
        var previous = Status;
        Status = summary;
        await Task.Delay(TimeSpan.FromSeconds(8));
        if (version == _loadVersion && NoticeRow == null && Status == summary) Status = previous;
    }

    /// <summary>点击固定提示目标，其他材料只临时覆盖显示。</summary>
    [RelayCommand]
    private void ShowStatus(AccountRecordsInventoryRow? row)
    {
        if (row == null || IsLoading || !Rows.Contains(row)) return;
        _selectedNoticeRow = row;
        DisplayNotice(row);
    }

    /// <summary>悬停展示当前材料，保留已点选的提示目标。</summary>
    [RelayCommand]
    private void PreviewStatus(AccountRecordsInventoryRow? row)
    {
        if (row != null) DisplayNotice(row);
    }

    /// <summary>移开时恢复点选材料；尚未点选则保留最后一次悬停提示。</summary>
    [RelayCommand]
    private void EndStatusPreview()
    {
        if (_selectedNoticeRow != null) DisplayNotice(_selectedNoticeRow);
    }

    /// <summary>同步底部文本、操作目标与行高亮。</summary>
    private void DisplayNotice(AccountRecordsInventoryRow row)
    {
        if (IsLoading || !Rows.Contains(row)) return;
        NoticeRow = row; _noticeUid = Uid;
        Status = row.Event?.Message ?? row.Error?.Message ?? "";
        NoticeDetails = row.StatusDetails;
    }

    /// <summary>打开事件集合，跳转本身不归档；窗口内可执行已完成事件批量归档。</summary>
    [RelayCommand]
    private async Task OpenEvents(string? scope)
    {
        var uid = scope == "all" ? Uid : _noticeUid ?? Uid;
        if (uid == null) return;
        // 读取前固定提示目标，避免异步期间悬停变化导致定位到其他物品。
        var notice = scope == "all" ? null : NoticeRow;
        try
        {
            var workspace = await _store.ReadAsync(uid);
            new AccountRecordsEventsDialog(new AccountRecordsEventsViewModel(workspace.Inventory, notice?.Event?.Id, async (kind, ids) =>
            {
                await _store.ArchiveCompletedEventsAsync(uid, kind, ids);
                if (Uid == uid) await RefreshRowsAsync();
                return (await _store.ReadAsync(uid)).Inventory;
            }, notice?.Name))
            { Owner = Application.Current.MainWindow }.ShowDialog();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>关闭仅收起提示和高亮；归档由独立操作执行，不改变事件状态。</summary>
    [RelayCommand]
    private void CloseNotice()
    {
        Status = ""; NoticeDetails = ""; NoticeRow = null; _noticeUid = null; _selectedNoticeRow = null;
    }

    /// <summary>底部执行明确的错误处理，拒绝绕过核实条件。</summary>
    [RelayCommand]
    private async Task ResolveNotice(string action)
    {
        if (_noticeUid == null || NoticeRow?.Error == null) return;
        if (action == "accept" && !CanAcceptNotice)
        {
            Status = "当前展示的错误不能接受，请查看事件记录或手动修订库存。";
            return;
        }
        var uid = _noticeUid; var row = NoticeRow;
        await RunManualAsync(() => _store.ResolveAsync(uid, row.Name, row.Error.Id, action == "accept", row.Event?.Id));
    }

    /// <summary>完成提示只归档本物品同类合法事件；其他状态保留原有确认逻辑。</summary>
    [RelayCommand]
    private async Task AcknowledgeNotice()
    {
        if (_noticeUid == null || NoticeRow?.Event == null || !CanAcknowledgeNotice) return;
        var uid = _noticeUid; var row = NoticeRow;
        if (row.Event.State == "Completed")
        {
            var kind = row.Event.Kind;
            var ids = InventoryEventPolicy.GetCompletedArchiveCandidates(_workspace.Inventory, kind)
                .Where(item => item.Name == row.Name).Select(item => item.Id).ToArray();
            await RunManualAsync(() => _store.ArchiveCompletedEventsAsync(uid, kind, ids));
            return;
        }
        await RunManualAsync(async () =>
        {
            foreach (var item in _workspace.Inventory.Events.Where(item => item.Name == row.Name && item.State == row.Event.State && !item.Archived))
                await _store.UpdateEventAsync(uid, item.Id, false, true);
        });
    }

    /// <summary>人工写入经统一执行入口检查；展示刷新不继续持有人工操作锁。</summary>
    private Task RunManualAsync(Func<Task> action, AccountRecordsRefreshKind refresh = AccountRecordsRefreshKind.Content) =>
        RunDisplayAsync(() => AccountRecordsInteractionGate.Instance.ExecuteManualAsync(action), refresh);

    /// <summary>执行展示操作并刷新行，不隐含人工写入权限或重新排序。</summary>
    private async Task RunDisplayAsync(Func<Task> action, AccountRecordsRefreshKind refresh = AccountRecordsRefreshKind.Content)
    {
        var uid = Uid;
        var version = _loadVersion;
        try
        {
            await action();
            if (Uid == uid && version == _loadVersion) await RefreshRowsAsync(refresh);
            OnPropertyChanged(nameof(PreferReadableSource)); OnPropertyChanged(nameof(SnapshotsEnabled));
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(ex.Message, "账号记录", MessageBoxButton.OK, ThemedMessageBox.MessageBoxIcon.Error); }
    }

    /// <summary>打开数量编辑；已收录名称与内置非空分类只读，用户补充分类允许修订。</summary>
    [RelayCommand] private async Task Edit(AccountRecordsInventoryRow? row)
    {
        if (Uid == null || row == null) return;
        var uid = Uid;
        var editor = new AccountRecordsInventoryEditViewModel(_store, _catalog, uid, row, _workspace.Definitions);
        var dialog = new AccountRecordsInventoryEditDialog(editor) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && Uid == uid) await RefreshRowsAsync();
    }

    /// <summary>无扫描数据时也可由用户显式新增，不创建演示数据。</summary>
    [RelayCommand] private async Task Add()
    {
        if (Uid == null) return;
        var uid = Uid;
        var editor = new AccountRecordsInventoryEditViewModel(_store, _catalog, uid, null, _workspace.Definitions);
        var dialog = new AccountRecordsInventoryEditDialog(editor) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && Uid == uid) await RefreshRowsAsync();
    }

    /// <summary>展示各来源最后一次上报，不解释为获得量历史。</summary>
    [RelayCommand]
    private void ShowRecent(AccountRecordsInventoryRow? row)
    {
        if (row == null) return;
        new AccountRecordsItemDetailsDialog(new AccountRecordsItemDetailsViewModel(row, _workspace.Sources, PreferReadableSource))
        { Owner = Application.Current.MainWindow }.ShowDialog();
    }
    /// <summary>删除单项及其快照副本，防止日后恢复时重新出现。</summary>
    [RelayCommand] private async Task Delete(AccountRecordsInventoryRow? row)
    {
        if (Uid == null || row == null) return;
        var uid = Uid;
        if (await ThemedMessageBox.ShowAsync($"删除“{row.Name}”的库存、异常和快照中的记录？", "删除库存记录", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            await RunManualAsync(() => _store.DeleteItemAsync(uid, row.Name));
    }

    /// <summary>保存供各模块共用的来源显示偏好，当前仅库存分页消费该设置。</summary>
    [RelayCommand] private Task ToggleSourceNames() => RunDisplayAsync(async () =>
    {
        _workspace.Settings.PreferReadableSource = !_workspace.Settings.PreferReadableSource;
        await _store.ConfigureAsync(_workspace.Settings);
    });

    /// <summary>开关日期快照，不删除已有快照。</summary>
    [RelayCommand] private Task ToggleSnapshots() => RunManualAsync(async () =>
    {
        _workspace.Settings.SnapshotsEnabled = !_workspace.Settings.SnapshotsEnabled;
        await _store.ConfigureAsync(_workspace.Settings);
    });

    /// <summary>编辑有明确上限的保存数量。</summary>
    [RelayCommand] private async Task SetRetention(string kind)
    {
        var snapshots = kind == "snapshots";
        var events = kind == "events";
        var previous = events ? _workspace.Settings.EventLimit : snapshots ? _workspace.Settings.SnapshotLimit : _workspace.Settings.RecentLimit;
        var prompt = events ? "每个 UID 保留多少条事件？（1000～50000，默认 5000）超限清理最旧的已结束记录；未解决事件保留，实际数量可能超过上限。"
            : snapshots ? "保留最近几个有记录的日期？（1～30）" : "每个物品保留几个最近来源？（1～10，含当前记录）";
        var value = PromptDialog.Prompt(prompt, "保存设置", previous.ToString(CultureInfo.InvariantCulture));
        if (string.IsNullOrEmpty(value)) return;
        if (!int.TryParse(value, out var count) || count < (events ? 1000 : 1) || count > (events ? 50000 : snapshots ? 30 : 10))
        { await ThemedMessageBox.ShowAsync("请输入范围内的整数。", "保存设置", MessageBoxButton.OK); return; }
        if (count < previous)
        {
            var records = events ? "已结束事件" : snapshots ? "日期快照" : "每个物品的来源记录";
            if (await ThemedMessageBox.ShowAsync(
                    $"将{records}保留数量从 {previous} 调小至 {count}，会立即清理所有 UID 中超出上限的旧记录。\n调大数量不会恢复已清理的记录。请先备份运行目录下的 User/AccountRecords 文件夹，再继续操作。\n\n是否继续？",
                    "减少保留数量", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning) != MessageBoxResult.Yes)
                return;
        }
        await RunManualAsync(async () =>
        {
            if (events) _workspace.Settings.EventLimit = count;
            else if (snapshots) _workspace.Settings.SnapshotLimit = count; else _workspace.Settings.RecentLimit = count;
            await _store.ConfigureAsync(_workspace.Settings);
        });
    }

    /// <summary>清理公共派生图片，不影响任何 UID 的库存。</summary>
    [RelayCommand] private async Task ClearIconCache()
    {
        if (await ThemedMessageBox.ShowAsync("清理 AccountRecords 的所有图像缓存？包括所有 UID、子表共用的物品图标及活动图片，清理后等待重建。库存和活动记录不受影响。",
            "清理所有图像缓存", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            await RunManualAsync(() => Task.Run(_icons.Clear), AccountRecordsRefreshKind.Icons);
    }

    /// <summary>按实际缓存文件判断删除入口；关闭图像显示或缓存损坏不影响清理。</summary>
    private bool CanDeleteIcon(AccountRecordsInventoryRow? row) => row != null && _icons.Exists(row.Name);

    /// <summary>删除单个物品图标；缓存跨 UID 共用，不删除库存记录。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteIcon))]
    private async Task DeleteIcon(AccountRecordsInventoryRow? row)
    {
        if (row == null || !CanDeleteIcon(row)) return;
        if (await ThemedMessageBox.ShowAsync($"清理“{row.Name}”的图标缓存？所有 UID 共用此图标，清理后等待重建，库存记录不受影响。",
            "清理物品图标缓存", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            await RunManualAsync(() => Task.Run(() => _icons.Delete(row.Name)), AccountRecordsRefreshKind.Icons);
    }

    /// <summary>选择本地图片替换共享缓存；后续扫描可重新生成。</summary>
    [RelayCommand]
    private async Task UploadIcon(AccountRecordsInventoryRow? row)
    {
        if (row == null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择物品图标（最多 4 MiB）", Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp", CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        await RunManualAsync(() => Task.Run(() =>
        {
            using var stream = File.OpenRead(dialog.FileName);
            if (stream.Length is <= 0 or > 4 * 1024 * 1024) throw new InvalidDataException("图片必须大于 0 字节且不超过 4 MiB。");
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            _icons.Store(row.Name, bytes);
        }), AccountRecordsRefreshKind.Icons);
    }
}
