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
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>库存分页的筛选、编辑、异常处理和保存设置。</summary>
public partial class AccountRecordsInventoryViewModel : ObservableObject
{
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
    public ObservableCollection<AccountRecordsInventoryRow> Rows { get; } = [];
    /// <summary>页面排序和筛选视图。</summary>
    public ICollectionView Items { get; }
    /// <summary>是否没有匹配行。</summary>
    public bool IsEmpty => Items.IsEmpty;
    /// <summary>是否选择了账号。</summary>
    public bool HasUid => Uid != null;
    /// <summary>默认显示来源名称。</summary>
    public bool PreferReadableSource => _workspace.Settings.PreferReadableSource;
    /// <summary>快照开关状态。</summary>
    public bool SnapshotsEnabled => _workspace.Settings.SnapshotsEnabled;
    /// <summary>仅在存在异常时显示的底部提示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";
    /// <summary>正常状态不占用底栏空间。</summary>
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    /// <summary>当前底部提示对应行。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKeepNotice))]
    [NotifyPropertyChangedFor(nameof(ShowBatchArchive))]
    [NotifyPropertyChangedFor(nameof(CanResolveNotice))]
    [NotifyPropertyChangedFor(nameof(CanAcceptNotice))]
    [NotifyPropertyChangedFor(nameof(CanAcknowledgeNotice))]
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
    /// <summary>绿灯可保留。</summary>
    public bool CanKeepNotice => NoticeRow?.StateRank == 3;
    /// <summary>当前错误允许舍弃。</summary>
    public bool CanResolveNotice => NoticeRow?.Error != null;
    /// <summary>只有普通记录冲突可主动接受，过期核实不能绕过。</summary>
    public bool CanAcceptNotice => NoticeRow?.Error?.CanAccept == true;
    /// <summary>绿灯或无库存冲突的拒绝增量可明确确认归档。</summary>
    public bool CanAcknowledgeNotice => NoticeRow?.Event != null && (NoticeRow.StateRank is 1 or 3 || NoticeRow.StateRank == 0 && NoticeRow.Error == null);

    /// <summary>直接打开规则 JSON，首次创建默认配置，不自动启用追踪。</summary>
    [RelayCommand]
    private async Task EditWarningRules()
    {
        try
        {
            var path = await _store.GetWarningRulesPathAsync();
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
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
        Items = CollectionViewSource.GetDefaultView(Rows);
        Items.Filter = value => value is AccountRecordsInventoryRow row &&
            (!HasCategoryFilter || Categories.Any(category => category.Name == row.Category && category.IsSelected)) &&
            (row.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || row.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        Items.SortDescriptions.Add(new SortDescription(nameof(AccountRecordsInventoryRow.RecordedAt), ListSortDirection.Descending));
    }

    /// <summary>立即更新关键词筛选。</summary>
    partial void OnSearchTextChanged(string value) => Filter();
    /// <summary>统一更新全选状态、漏斗颜色和过滤结果。</summary>
    private void CategoriesChanged()
    {
        if (_updatingCategories) return;
        OnPropertyChanged(nameof(HasCategoryFilter)); OnPropertyChanged(nameof(AllCategoriesSelected));
        Filter();
    }
    /// <summary>同步完整字典分类；唯一选中的分类消失时恢复全选，避免失效筛选隐藏全部库存。</summary>
    private void UpdateCategories(System.Collections.Generic.IEnumerable<string> categories)
    {
        var names = categories.Select(value => string.IsNullOrEmpty(value) ? "未分类" : value).Append("未分类").Distinct().OrderBy(value => value).ToArray();
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
    /// <summary>通知空状态变化。</summary>
    private void Filter() { Items.Refresh(); OnPropertyChanged(nameof(IsEmpty)); }

    /// <summary>首次进入时恢复偏好，不要求已有 UID。</summary>
    public async Task LoadPreferencesAsync()
    {
        _workspace.Settings = await _store.GetSettingsAsync();
        // 提醒规则尚未开放，进入页面时关闭旧版本保存的追踪开关。
        if (_workspace.Settings.WarningTrackingEnabled)
        {
            _workspace.Settings.WarningTrackingEnabled = false;
            await _store.ConfigureAsync(_workspace.Settings);
        }
        var categories = await Task.Run(() => _catalog.Items.Values.ToArray());
        UpdateCategories(categories);
        OnPropertyChanged(nameof(PreferReadableSource)); OnPropertyChanged(nameof(SnapshotsEnabled));
    }

    /// <summary>切换 UID 并丢弃过期异步读取结果。</summary>
    public async Task SelectUidAsync(string? uid)
    {
        var changedUid = Uid != uid;
        Uid = uid; OnPropertyChanged(nameof(HasUid));
        OnPropertyChanged(nameof(HasCompletedEvents));
        OnPropertyChanged(nameof(ShowBatchArchive));
        var version = ++_loadVersion;
        if (changedUid) { Rows.Clear(); OnPropertyChanged(nameof(IsEmpty)); }
        Status = "";
        NoticeRow = null; NoticeDetails = ""; _noticeUid = null; _selectedNoticeRow = null;
        if (uid == null) return;
        try
        {
            var workspace = await _store.LoadAsync(uid);
            var rows = await Task.Run(() => workspace.Inventory.Items.Keys.Union(workspace.Inventory.Errors.Keys, StringComparer.Ordinal)
                .Union(workspace.Inventory.Events.Where(item => !item.Archived).Select(item => item.Name), StringComparer.Ordinal).Select(name =>
            {
                workspace.Inventory.Items.TryGetValue(name, out var recent);
                workspace.Inventory.Errors.TryGetValue(name, out var error);
                var current = recent?.FirstOrDefault();
                var reference = current?.Source ?? error?.Source ?? "";
                workspace.Sources.TryGetValue(reference, out var source);
                workspace.Definitions.TryGetValue(name, out var category);
                var events = workspace.Inventory.Events.Where(item => item.Name == name && !item.Archived).ToArray();
                var active = events.OrderBy(item => item.State == "Error" ? 0 : item.State == "Warning" ? 1 : item.State == "Pending" ? 2 : item.State == "Completed" ? 3 : 4).ThenByDescending(item => item.Id).FirstOrDefault();
                var pending = events.Where(item => item.Kind == "Delta" && item.State == "Pending").ToArray();
                var changes = active?.Kind == "Delta" ? events.Where(item => item.Kind == "Delta" && item.State == active.State &&
                    (active.State != "Completed" || item.VerificationId == active.VerificationId)).ToArray() : [];
                string Display(string key) => workspace.Sources.TryGetValue(key, out var descriptor) && workspace.Settings.PreferReadableSource ? descriptor.DisplayName : key;
                var origins = changes.Select(item => Display(item.Source)).Distinct().ToArray();
                var firstOrigin = origins.FirstOrDefault() ?? Display(active?.Source ?? reference);
                if (firstOrigin.Length > 24) firstOrigin = firstOrigin[..24] + "…";
                var details = changes.Length > 0 ? $"变动来源：{firstOrigin}{(origins.Length > 1 ? $"，共 {origins.Length} 处" : "")}，累计变动：{changes.Sum(item => (decimal)item.Quantity):+0;-0;±0}" : $"核实来源：{firstOrigin}";
                if (active?.State == "Completed") details += $"，结果：{(active.VerifiedCorrect == true ? "正确" : "错误")}，核实来源：{Display(active.VerificationSource)}";
                if (active?.Kind == "SourceWarning")
                    details = $"核实来源：{Display(active.Source)}，归档后重新统计";
                return new AccountRecordsInventoryRow
                {
                    LastScanAttemptId = workspace.Sources.Values.SelectMany(item => item.Attempts)
                        .Where(attempt => attempt.Uid == uid && ((recent?.Any(record => record.AttemptId == attempt.Id) == true) ||
                            workspace.Inventory.Events.Any(item => item.Name == name && item.AttemptId == attempt.Id)))
                        .OrderByDescending(attempt => attempt.CreatedAt).FirstOrDefault()?.Id,
                    Event = active, PendingDelta = pending.Length > 0 ? pending.Sum(item => (decimal)item.Quantity) : null, StatusDetails = details,
                    Name = name, Category = string.IsNullOrEmpty(category) ? "未分类" : category,
                    Quantity = current?.Quantity, RecordedAt = current?.RecordedAt,
                    SourceReference = reference,
                    SourceName = workspace.Settings.PreferReadableSource && !string.IsNullOrWhiteSpace(source?.DisplayName) ? source.DisplayName : reference,
                    Icon = _icons.Load(name), Error = error, Recent = recent ?? []
                };
            }).ToArray());
            if (version != _loadVersion) return;
            _workspace = workspace;
            OnPropertyChanged(nameof(HasCompletedEvents));
        OnPropertyChanged(nameof(ShowBatchArchive));
            UpdateCategories(workspace.Definitions.Values);
            Rows.Clear();
            // 集合变更会让 DataGrid 同步查询视图；不能在 DeferRefresh 期间逐项添加。
            foreach (var row in rows) Rows.Add(row);
            var errors = Rows.Count(row => row.StateRank == 0);
            Status = errors > 0 ? $"{errors} 项库存异常待处理，请点击状态灯查看。" : "";
            OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(PreferReadableSource)); OnPropertyChanged(nameof(SnapshotsEnabled));
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
                Status = $"库存读取失败：{ex.Message}" + (Rows.Count > 0 ? " 当前保留上次读取的内容，请刷新重试。" : " 请刷新重试。");
        }
    }

    /// <summary>当前账号存在可批量归档的已完成事件。</summary>
    public bool HasCompletedEvents => Uid != null && _workspace.Inventory.Events.Count(item => item.State == "Completed" && !item.Archived) >= 2;
    /// <summary>仅在绿色提示中展示批量归档，且至少有两个可归档事件。</summary>
    public bool ShowBatchArchive => NoticeRow?.StateRank == 3 && HasCompletedEvents;

    /// <summary>只归档当前账号的绿色事件，包括主动保留的已完成提示。</summary>
    [RelayCommand]
    private async Task ArchiveCompletedEvents()
    {
        if (Uid is not { } uid) return;
        await Run(async () => { await _store.ArchiveCompletedEventsAsync(uid); });
    }
    /// <summary>在只读窗口查看字典，用户与来源字典可显式开启外部编辑。</summary>
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
        var title = kind switch { "items" => "物品字典", "user" => "用户字典", _ => "来源字典" };
        new AccountRecordsDictionaryDialog(new AccountRecordsDictionaryViewModel(title, path, kind != "items", _catalog))
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
        if (row == null) return;
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
        NoticeRow = row; _noticeUid = Uid;
        Status = row.Event?.Message ?? row.Error?.Message ?? "";
        NoticeDetails = row.StatusDetails;
    }

    /// <summary>只读打开事件集合，日志跳转不确认提示。</summary>
    [RelayCommand]
    private async Task OpenEvents(string? scope)
    {
        var uid = scope == "all" ? Uid : _noticeUid ?? Uid;
        if (uid == null) return;
        try
        {
            var workspace = await _store.LoadAsync(uid);
            new AccountRecordsEventsDialog(new AccountRecordsEventsViewModel(workspace.Inventory.Events, scope == "all" ? null : NoticeRow?.Event?.Id, async () =>
            {
                await _store.ArchiveCompletedEventsAsync(uid);
                if (Uid == uid) await SelectUidAsync(uid);
                return (await _store.LoadAsync(uid)).Inventory.Events;
            }))
            { Owner = Application.Current.MainWindow }.ShowDialog();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>保留当前绿灯，不撤销核实结果。</summary>
    [RelayCommand]
    private async Task KeepNotice()
    {
        if (_noticeUid == null || NoticeRow?.Event == null) return;
        var uid = _noticeUid; var name = NoticeRow.Name;
        await Run(async () =>
        {
            foreach (var item in _workspace.Inventory.Events.Where(item => item.Name == name && item.State == "Completed" && !item.Archived))
                await _store.UpdateEventAsync(uid, item.Id, true, false);
        });
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
        var uid = _noticeUid; var row = NoticeRow;
        await Run(() => _store.ResolveAsync(uid, row.Name, row.Error.Id, action == "accept"));
    }

    /// <summary>明确确认可以结束已保留的绿灯，或归档被拒绝的增量事件。</summary>
    [RelayCommand]
    private async Task AcknowledgeNotice()
    {
        if (_noticeUid == null || NoticeRow?.Event == null || !CanAcknowledgeNotice) return;
        var uid = _noticeUid; var row = NoticeRow;
        await Run(async () =>
        {
            foreach (var item in _workspace.Inventory.Events.Where(item => item.Name == row.Name && item.State == row.Event.State && !item.Archived))
                await _store.UpdateEventAsync(uid, item.Id, false, true);
        });
    }

    /// <summary>统一处理操作失败并重载持久化结果。</summary>
    private async Task Run(Func<Task> action)
    {
        try
        {
            await action(); await SelectUidAsync(Uid);
            OnPropertyChanged(nameof(PreferReadableSource)); OnPropertyChanged(nameof(SnapshotsEnabled));
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(ex.Message, "账号记录", MessageBoxButton.OK, ThemedMessageBox.MessageBoxIcon.Error); }
    }

    /// <summary>打开数量编辑；合法名称与字典已有分类不可编辑。</summary>
    [RelayCommand] private async Task Edit(AccountRecordsInventoryRow? row)
    {
        if (Uid == null || row == null) return;
        var uid = Uid;
        var editor = new AccountRecordsInventoryEditViewModel(_store, _catalog, uid, row, _workspace.Definitions);
        var dialog = new AccountRecordsInventoryEditDialog(editor) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true) await SelectUidAsync(Uid);
    }

    /// <summary>无扫描数据时也可由用户显式新增，不创建演示数据。</summary>
    [RelayCommand] private async Task Add()
    {
        if (Uid == null) return;
        var editor = new AccountRecordsInventoryEditViewModel(_store, _catalog, Uid, null, _workspace.Definitions);
        var dialog = new AccountRecordsInventoryEditDialog(editor) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true) await SelectUidAsync(Uid);
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
            await Run(() => _store.DeleteItemAsync(uid, row.Name));
    }

    /// <summary>统一切换三个模块的来源显示偏好。</summary>
    [RelayCommand] private Task ToggleSourceNames() => Run(async () =>
    {
        _workspace.Settings.PreferReadableSource = !_workspace.Settings.PreferReadableSource;
        await _store.ConfigureAsync(_workspace.Settings);
    });

    /// <summary>开关日期快照，不删除已有快照。</summary>
    [RelayCommand] private Task ToggleSnapshots() => Run(async () =>
    {
        _workspace.Settings.SnapshotsEnabled = !_workspace.Settings.SnapshotsEnabled;
        await _store.ConfigureAsync(_workspace.Settings);
    });

    /// <summary>编辑有明确上限的保存数量。</summary>
    [RelayCommand] private async Task SetRetention(string kind)
    {
        var snapshots = kind == "snapshots";
        var events = kind == "events";
        var previous = events ? _workspace.Settings.ArchivedEventLimit : snapshots ? _workspace.Settings.SnapshotLimit : _workspace.Settings.RecentLimit;
        var prompt = events ? "每个 UID 保留多少条归档事件？（100～10000，默认 1000）超过上限自动清理最旧归档，活动事件不受影响。"
            : snapshots ? "保留最近几个有记录的日期？（1～30）" : "每个物品保留几个最近来源？（1～10，含当前记录）";
        var value = PromptDialog.Prompt(prompt, "保存设置", previous.ToString(CultureInfo.InvariantCulture));
        if (string.IsNullOrEmpty(value)) return;
        if (!int.TryParse(value, out var count) || count < (events ? 100 : 1) || count > (events ? 10000 : snapshots ? 30 : 10))
        { await ThemedMessageBox.ShowAsync("请输入范围内的整数。", "保存设置", MessageBoxButton.OK); return; }
        if (count < previous)
        {
            var records = events ? "归档事件" : snapshots ? "日期快照" : "每个物品的来源记录";
            if (await ThemedMessageBox.ShowAsync(
                    $"将{records}保留数量从 {previous} 调小至 {count}，会立即清理所有 UID 中超出上限的旧记录。\n调大数量不会恢复已清理的记录。请先备份运行目录下的 User/AccountRecords 文件夹，再继续操作。\n\n是否继续？",
                    "减少保留数量", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning) != MessageBoxResult.Yes)
                return;
        }
        await Run(async () =>
        {
            if (events) _workspace.Settings.ArchivedEventLimit = count;
            else if (snapshots) _workspace.Settings.SnapshotLimit = count; else _workspace.Settings.RecentLimit = count;
            await _store.ConfigureAsync(_workspace.Settings);
        });
    }

    /// <summary>清理公共派生图片，不影响任何 UID 的库存。</summary>
    [RelayCommand] private async Task ClearIconCache()
    {
        if (await ThemedMessageBox.ShowAsync("清理所有图标缓存？所有 UID 共用这些图标，清理后将显示占位图，等待后续重建。库存记录不受影响。",
            "清理所有图标缓存", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            await Run(() => Task.Run(_icons.Clear));
    }

    /// <summary>删除单个物品图标；缓存跨 UID 共用，不删除库存记录。</summary>
    [RelayCommand]
    private async Task DeleteIcon(AccountRecordsInventoryRow? row)
    {
        if (row == null) return;
        if (await ThemedMessageBox.ShowAsync($"清理“{row.Name}”的图标缓存？所有 UID 共用此图标，清理后等待重建，库存记录不受影响。",
            "清理物品图标缓存", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            await Run(() => Task.Run(() => _icons.Delete(row.Name)));
    }

    /// <summary>用户明确确认后，用本地图片替换共享缓存；后续扫描可重新生成。</summary>
    [RelayCommand]
    private async Task UploadIcon(AccountRecordsInventoryRow? row)
    {
        if (row == null) return;
        if (await ThemedMessageBox.ShowAsync(
            $"不推荐手动上传图标，建议等待扫描自动重建。\n\n上传仅替换“{row.Name}”的显示缓存，所有 UID 共用，不改变物品名称或识别结果；后续重建可能覆盖它。\n\n仍要选择图片吗？",
            "上传物品图标", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning) != MessageBoxResult.Yes) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择物品图标（最多 4 MiB）", Filter = "图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp", CheckFileExists = true
        };
        if (dialog.ShowDialog() != true) return;
        await Run(() => Task.Run(() =>
        {
            using var stream = File.OpenRead(dialog.FileName);
            if (stream.Length is <= 0 or > 4 * 1024 * 1024) throw new InvalidDataException("图片必须大于 0 字节且不超过 4 MiB。");
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            _icons.Store(row.Name, bytes);
        }));
    }
}
