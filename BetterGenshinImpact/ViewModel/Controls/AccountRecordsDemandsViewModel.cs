using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Controls;

/// <summary>材料需求分页：读取库存证据，独立管理目标、筛选与人工确认。</summary>
public partial class AccountRecordsDemandsViewModel : ObservableObject
{
    /// <summary>账号存储。</summary>
    private readonly InventoryStore _store;
    /// <summary>物品字典与中文排序。</summary>
    private readonly InventoryCatalog _catalog;
    /// <summary>与库存共享的图标缓存。</summary>
    private readonly InventoryIconCache _icons;
    /// <summary>读取代次，防止旧账号异步结果覆盖新账号。</summary>
    private int _loadVersion;
    /// <summary>区分同账号后台读取，较旧请求不得覆盖较新结果。</summary>
    private int _refreshVersion;
    /// <summary>当前行已应用的共享图标修订。</summary>
    private long _iconCacheRevision;
    /// <summary>首次读取成功后，返回页面采用原位更新。</summary>
    private bool _hasSnapshot;
    /// <summary>批量更新分类时合并通知。</summary>
    private bool _updatingCategories;
    /// <summary>最近读取的公共偏好。</summary>
    private AccountRecordsSettings _settings = new();
    /// <summary>当前账号；没有账号时禁用写入。</summary>
    public string? Uid { get; private set; }
    /// <summary>完整物品并集，批量替换后统一刷新视图，避免逐条触发排序和布局。</summary>
    public List<AccountRecordsDemandRow> Rows { get; } = [];
    /// <summary>筛选和排序后的表格视图。</summary>
    public ICollectionView Items { get; }
    /// <summary>库存风格的背包页与材料类型分组。</summary>
    public ObservableCollection<AccountRecordsCategoryOption> Categories { get; } = [];
    /// <summary>加载期间禁用旧行操作。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUid))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(ShowPlaceholder))]
    private bool _isLoading;
    /// <summary>当前页操作消息。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(ShowStatusMessage))]
    private string _status = "";
    /// <summary>操作消息与需求卡片分开展示，空消息不占空间。</summary>
    public bool ShowStatusMessage => !string.IsNullOrEmpty(Status);
    /// <summary>当前是否展示需求状态卡片。</summary>
    public bool HasNotice => NoticeRow != null;
    /// <summary>底部提示是否占用布局空间。</summary>
    public bool HasStatus => !string.IsNullOrEmpty(Status) || NoticeRow != null;
    /// <summary>当前被点击固定的提示行。</summary>
    private AccountRecordsDemandRow? _selectedNoticeRow;
    /// <summary>当前悬停或点击显示的提示行。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private AccountRecordsDemandRow? _noticeRow;
    /// <summary>当前分页独立的搜索词。</summary>
    [ObservableProperty] private string _searchText = "";
    /// <summary>仅显示已创建需求的物品，默认保留全部库存项。</summary>
    [ObservableProperty] private bool _onlyDemands;
    /// <summary>是否可以创建或编辑需求。</summary>
    public bool HasUid => Uid != null && !IsLoading;
    /// <summary>没有匹配物品时展示空状态，不将读取失败误认为可写数据。</summary>
    public bool IsEmpty => !IsLoading && Items.IsEmpty;
    /// <summary>加载与空结果共用库存页的表体水印。</summary>
    public bool ShowPlaceholder => IsLoading || IsEmpty;
    /// <summary>沿用库存的缺图行号设置。</summary>
    public bool ShowMissingIconRowNumbers => _settings.ShowMissingIconRowNumbers;
    /// <summary>需求专用的目标登记时间列偏好。</summary>
    public bool ShowDemandCreatedAt => _settings.ShowDemandCreatedAt;

    /// <summary>切换目标登记时间列，不改变库存列设置。</summary>
    [RelayCommand]
    private async Task ToggleDemandCreatedAt()
    {
        try
        {
            _settings = await _store.UpdateSettingsAsync(settings => settings.ShowDemandCreatedAt = !settings.ShowDemandCreatedAt);
            OnPropertyChanged(nameof(ShowDemandCreatedAt));
        }
        catch (Exception ex) { Status = AccountRecordsErrorMessages.Describe(ex); }
    }

    /// <summary>玩家自行置顶需求页物品，库存页的置顶不受影响。</summary>
    [RelayCommand]
    private async Task TogglePrior(AccountRecordsDemandRow? row)
    {
        if (!HasUid || row == null || !Rows.Contains(row)) return;
        var uid = Uid!;
        await WriteAsync(uid, () => _store.SetDemandPriorAsync(uid, row.Item.Name, !row.Prior));
    }
    /// <summary>筛选后物品数量。</summary>
    public string ItemCountText => string.Format(I18nService.Instance.Translate("共 {0} 项，筛选后 {1} 项"), Rows.Count, Items.Cast<object>().Count());
    /// <summary>非全选时显示筛选激活状态。</summary>
    public bool HasCategoryFilter => Categories.Any(item => item.IsSelected != true);
    /// <summary>全选与取消全选。</summary>
    public bool AllCategoriesSelected
    {
        get => !HasCategoryFilter;
        set
        {
            _updatingCategories = true;
            try { foreach (var item in Categories) item.IsSelected = value; }
            finally { _updatingCategories = false; }
            CategoriesChanged();
        }
    }

    /// <summary>构造分页视图，不在构造阶段访问磁盘。</summary>
    public AccountRecordsDemandsViewModel(InventoryStore store, InventoryCatalog catalog, InventoryIconCache icons)
    {
        _store = store; _catalog = catalog; _icons = icons;
        Items = new ListCollectionView(Rows) { Culture = catalog.TextSortCulture };
        Items.Filter = value => value is AccountRecordsDemandRow row && (!OnlyDemands || row.HasDemand) &&
            (!HasCategoryFilter || Categories.SelectMany(group => group.Children).Any(option =>
                option.Page == row.Item.Page && option.Category == row.Item.MaterialType && option.IsSelected == true)) &&
            (row.Item.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
             row.Item.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
             row.Item.Page.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        // 仅按单项置顶、是否有需求及用户列顺序排列。
        ((ListCollectionView)Items).CustomSort = new AccountRecordsDemandComparer();
        Items.CollectionChanged += (_, _) => UpdateCounts();
    }

    /// <summary>搜索立即生效。</summary>
    partial void OnSearchTextChanged(string value) => Items.Refresh();
    /// <summary>需求范围变化时，沿用库存规则只保留该范围实际出现的类型。</summary>
    partial void OnOnlyDemandsChanged(bool value) => UpdateCategories();
    /// <summary>刷新行号和项数，不修改数据。</summary>
    private void UpdateCounts()
    {
        if (IsLoading) return;
        var priorNumber = 0;
        var otherNumber = 0;
        var visible = Items.Cast<AccountRecordsDemandRow>().ToArray();
        var boundary = visible.Any(row => !row.Prior) ? visible.LastOrDefault(row => row.Prior) : null;
        foreach (var row in Rows) row.ShowPriorSeparator = ReferenceEquals(row, boundary);
        foreach (var row in visible)
        {
            row.DisplayRowNumber = row.Prior ? (++priorNumber).ToString(CultureInfo.InvariantCulture) : $"{++otherNumber}.";
            row.Item.DisplayRowNumber = row.DisplayRowNumber;
        }
        OnPropertyChanged(nameof(ItemCountText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowPlaceholder));
    }
    /// <summary>分类变更后通知共享筛选器。</summary>
    private void CategoriesChanged()
    {
        if (_updatingCategories) return;
        OnPropertyChanged(nameof(HasCategoryFilter));
        OnPropertyChanged(nameof(AllCategoriesSelected));
        Items.Refresh();
    }
    /// <summary>恢复筛选器默认状态：全部分类和全部库存物品，不清空搜索。</summary>
    [RelayCommand] private void ResetCategories() { OnlyDemands = false; AllCategoriesSelected = true; }

    /// <summary>根据全部物品或仅需求范围生成分类；刷新保留勾选与展开状态，切换账号由调用方清空。</summary>
    private void UpdateCategories()
    {
        var previous = Categories.SelectMany(group => group.Children).ToDictionary(item => (item.Page, item.Category), item => item.IsSelected == true);
        var expansion = Categories.ToDictionary(item => item.Page, item => item.IsExpanded);
        // 与库存一致，类型来自当前数据范围而非搜索或勾选后的结果，避免筛选入口被自身隐藏。
        var keys = Rows.Where(row => !OnlyDemands || row.HasDemand)
            .Select(row => (row.Item.Page, Category: row.Item.MaterialType)).Distinct().ToArray();
        var all = AllCategoriesSelected || previous.Any(item => item.Value) && !keys.Any(key => previous.GetValueOrDefault(key));
        _updatingCategories = true;
        try
        {
            Categories.Clear();
            foreach (var group in keys.OrderBy(item => InventoryCatalog.GetPageSortOrder(item.Page))
                         .ThenBy(item => item.Page, _catalog.TextComparer).ThenBy(item => item.Category, _catalog.TextComparer).GroupBy(item => item.Page))
            {
                var option = new AccountRecordsCategoryOption(group.Key, group.Select(item => item.Category), CategoriesChanged);
                if (expansion.TryGetValue(group.Key, out var expanded)) option.IsExpanded = expanded;
                foreach (var child in option.Children) child.IsSelected = all || previous.GetValueOrDefault((child.Page, child.Category));
                Categories.Add(option);
            }
        }
        finally { _updatingCategories = false; }
        CategoriesChanged();
    }

    /// <summary>按账号读取当前库存、未归档事件与需求，不读取库存归档历史。</summary>
    public async Task SelectUidAsync(string? uid, bool load = true)
    {
        var version = ++_loadVersion;
        _hasSnapshot = false;
        if (uid != Uid) { Rows.Clear(); Categories.Clear(); Items.Refresh(); }
        Uid = uid;
        IsLoading = uid != null && load;
        OnPropertyChanged(nameof(HasUid));
        CloseNotice();
        if (uid == null || !load) { Rows.Clear(); Categories.Clear(); IsLoading = false; Items.Refresh(); return; }
        try
        {
            var iconRevision = _icons.Revision;
            var (workspace, demands) = await _store.ReadDemandWorkspaceAsync(uid);
            var inventoryRows = await new AccountRecordsInventoryRowFactory(_catalog, _icons).BuildAsync(workspace);
            var rows = await Task.Run(() => BuildRows(workspace, demands, inventoryRows));
            if (version != _loadVersion) return;
            _settings = workspace.Settings;
            _iconCacheRevision = iconRevision;
            // ListCollectionView 的延迟刷新不支持在此期间逐条变更可观察源；一次替换后再刷新。
            Rows.Clear();
            Rows.AddRange(rows);
            _hasSnapshot = true;
            UpdateCategories();
            OnPropertyChanged(nameof(ShowMissingIconRowNumbers));
            OnPropertyChanged(nameof(ShowDemandCreatedAt));
        }
        catch (Exception ex)
        {
            if (version == _loadVersion) Status = I18nService.Instance.Translate("需求读取失败：") + AccountRecordsErrorMessages.Describe(ex);
        }
        finally
        {
            if (version == _loadVersion) { IsLoading = false; UpdateCounts(); }
        }
    }

    /// <summary>同账号重新可见或设置变化时原位读取，不显示加载遮罩或关闭提示。</summary>
    public async Task ResumeAsync(string? uid)
    {
        if (uid != Uid || !_hasSnapshot)
        {
            await SelectUidAsync(uid);
            return;
        }
        if (uid == null || IsLoading) return;
        var version = _loadVersion;
        var refreshVersion = ++_refreshVersion;
        var iconRevision = _icons.Revision;
        try
        {
            var (workspace, demands) = await _store.ReadDemandWorkspaceAsync(uid);
            var inventoryRows = await new AccountRecordsInventoryRowFactory(_catalog, _icons).BuildAsync(workspace);
            var next = await Task.Run(() => BuildRows(workspace, demands, inventoryRows));
            if (Uid != uid || version != _loadVersion || refreshVersion != _refreshVersion || IsLoading) return;
            var existing = Rows.ToDictionary(row => row.Item.Name, StringComparer.Ordinal);
            var names = next.Select(row => row.Item.Name).ToHashSet(StringComparer.Ordinal);
            var refreshView = next.Length != Rows.Count;
            foreach (var row in next)
            {
                if (existing.TryGetValue(row.Item.Name, out var current))
                {
                    refreshView |= current.HasDemand != row.HasDemand || current.Prior != row.Prior ||
                        current.Item.Page != row.Item.Page || current.Item.MaterialType != row.Item.MaterialType ||
                        // 原位更新不会触发实时排序；比较器读取的值变化时刷新视图。
                        current.Item.Quantity != row.Item.Quantity || current.Item.RecordedAt != row.Item.RecordedAt ||
                        current.TargetQuantity != row.TargetQuantity || current.CreatedAt != row.CreatedAt ||
                        current.Source != row.Source || current.StateRank != row.StateRank ||
                        current.Item.ItemNameSortKey != row.Item.ItemNameSortKey || current.CategorySortKey != row.CategorySortKey;
                    current.ApplySnapshot(row, iconRevision != _iconCacheRevision);
                }
                else { Rows.Add(row); refreshView = true; }
            }
            Rows.RemoveAll(row => !names.Contains(row.Item.Name));
            _iconCacheRevision = iconRevision;
            _settings = workspace.Settings;
            // 不变的类型集合保留原筛选控件及其展开状态，避免每分钟重建筛选器。
            var keys = Rows.Where(row => !OnlyDemands || row.HasDemand)
                .Select(row => (row.Item.Page, row.Item.MaterialType)).ToHashSet();
            var previous = Categories.SelectMany(group => group.Children)
                .Select(option => (option.Page, option.Category)).ToHashSet();
            if (!keys.SetEquals(previous)) UpdateCategories();
            else if (refreshView) Items.Refresh();
            if (_selectedNoticeRow != null && (!Rows.Contains(_selectedNoticeRow) || !_selectedNoticeRow.HasDemand))
                _selectedNoticeRow = null;
            if (NoticeRow != null && (!Rows.Contains(NoticeRow) || !NoticeRow.HasDemand))
            {
                NoticeRow.IsNoticeActive = false;
                NoticeRow = null;
                if (_selectedNoticeRow != null) DisplayNotice(_selectedNoticeRow);
            }
            OnPropertyChanged(nameof(ShowMissingIconRowNumbers));
            OnPropertyChanged(nameof(ShowDemandCreatedAt));
            UpdateCounts();
        }
        catch (Exception ex)
        {
            if (Uid == uid && version == _loadVersion && refreshVersion == _refreshVersion)
                Status = I18nService.Instance.Translate("需求读取失败：") + AccountRecordsErrorMessages.Describe(ex);
        }
    }

    /// <summary>按当前库存和需求构建展示行，集中计算各项目标进度。</summary>
    private AccountRecordsDemandRow[] BuildRows(InventoryWorkspace workspace, MaterialDemandDocument demands, AccountRecordsInventoryRow[] inventoryRows)
    {
        var items = inventoryRows.ToDictionary(row => row.Name, StringComparer.Ordinal);
        // 仅补充确有需求的物品，不因同族关系扩展展示行。
        var requiredNames = demands.Items.Keys;
        foreach (var name in requiredNames.Where(name => !items.ContainsKey(name)))
            items[name] = new AccountRecordsInventoryRow
            {
                Name = name, ItemNameSortKey = _catalog.GetItemNameSortKey(name),
                MaterialType = workspace.Definitions.GetValueOrDefault(name, ""),
                Category = workspace.Definitions.GetValueOrDefault(name, I18nService.Instance.Translate("未分类")),
                Page = workspace.Pages.GetValueOrDefault(name, ""),
                IconLoader = workspace.Settings.UseImageCache ? () => _icons.Load(name) : null
            };
        var progressByName = MaterialDemandPolicy.EvaluateAll(workspace, demands);
        var rows = items.Values.Select(item =>
        {
            // 此处是需求页独立创建的展示行，覆盖其置顶只影响本页图标和排序。
            item.Prior = demands.Prior.Contains(item.Name);
            demands.Items.TryGetValue(item.Name, out var demand);
            var progress = progressByName.GetValueOrDefault(item.Name);
            var reference = demand?.Source ?? "";
            var source = !workspace.Settings.PreferReadableSource ? reference
                : workspace.Sources.TryGetValue(reference, out var descriptor) && !string.IsNullOrWhiteSpace(descriptor.DisplayName)
                    ? descriptor.DisplayName : reference;
            return new AccountRecordsDemandRow { Item = item, Demand = demand, Progress = progress, Source = source };
        }).ToArray();
        return rows;
    }

    /// <summary>复用库存的物品详情和编辑操作，执行后重新读取需求证据。</summary>
    private async Task InventoryAction(AccountRecordsDemandRow? row, string action)
    {
        if (!HasUid || IsLoading || row == null || !Rows.Contains(row)) return;
        var uid = Uid!;
        try
        {
            // 使用独立操作上下文，避免改变库存分页的筛选、排序和通知状态。
            var inventory = new AccountRecordsInventoryViewModel(_store, _catalog, _icons);
            await inventory.SelectUidAsync(uid);
            if (Uid != uid) return;
            var item = inventory.Rows.FirstOrDefault(item => item.Name == row.Item.Name) ?? row.Item;
            switch (action)
            {
                case "details": inventory.ShowRecentCommand.Execute(item); break;
                case "edit": await inventory.EditCommand.ExecuteAsync(item); break;
            }
            if (Uid == uid) await ResumeAsync(uid);
        }
        catch (Exception ex) { if (Uid == uid) Status = AccountRecordsErrorMessages.Describe(ex); }
    }

    /// <summary>查看库存详情与保留的批次历史。</summary>
    [RelayCommand] private Task ShowRecent(AccountRecordsDemandRow? row) => InventoryAction(row, "details");
    /// <summary>修改的是库存数量或字典，不修改需求目标。</summary>
    [RelayCommand] private Task EditInventory(AccountRecordsDemandRow? row) => InventoryAction(row, "edit");

    /// <summary>主动刷新完整重新读取，不产生模拟库存。</summary>
    [RelayCommand] private Task Refresh() => SelectUidAsync(Uid);
    /// <summary>可见分页定时核实，原位更新并保留当前交互状态。</summary>
    [RelayCommand] private Task RefreshTime() => ResumeAsync(Uid);

    /// <summary>点击状态灯固定需求提示，沿用库存页交互。</summary>
    [RelayCommand]
    private void ShowStatus(AccountRecordsDemandRow? row)
    {
        if (IsLoading || row?.HasDemand != true || !Rows.Contains(row)) return;
        _selectedNoticeRow = row;
        DisplayNotice(row);
    }

    /// <summary>悬停暂时查看其他需求，不改变固定目标。</summary>
    [RelayCommand]
    private void PreviewStatus(AccountRecordsDemandRow? row)
    {
        if (!IsLoading && row?.HasDemand == true && Rows.Contains(row)) DisplayNotice(row);
    }

    /// <summary>移开时恢复点击固定的提示。</summary>
    [RelayCommand] private void EndStatusPreview() { if (_selectedNoticeRow != null) DisplayNotice(_selectedNoticeRow); }

    /// <summary>高亮与底部文字始终指向同一物品。</summary>
    private void DisplayNotice(AccountRecordsDemandRow row)
    {
        if (_noticeRow != null) _noticeRow.IsNoticeActive = false;
        NoticeRow = row;
        row.IsNoticeActive = true;
        Status = "";
    }

    /// <summary>收起提示仅改变展示，不确认或删除需求。</summary>
    [RelayCommand]
    private void CloseNotice()
    {
        if (_noticeRow != null) _noticeRow.IsNoticeActive = false;
        _selectedNoticeRow = null;
        NoticeRow = null;
        Status = "";
    }

    /// <summary>从任意标准名称创建需求，允许尚无库存记录的物品。</summary>
    [RelayCommand]
    private async Task Add()
    {
        if (!HasUid) return;
        var uid = Uid!;
        var name = PromptDialog.Prompt(I18nService.Instance.Translate("请输入物品标准名称"), I18nService.Instance.Translate("创建材料需求"))?.Trim();
        if (string.IsNullOrEmpty(name) || Uid != uid) return;
        await EditTargetAsync(uid, name, Rows.FirstOrDefault(row => row.Item.Name == name)?.Demand);
    }

    /// <summary>右键无需求行创建，有需求行修改目标。</summary>
    [RelayCommand]
    private async Task Edit(AccountRecordsDemandRow? row)
    {
        if (!HasUid || row == null || !Rows.Contains(row)) return;
        await EditTargetAsync(Uid!, row.Item.Name, row.Demand);
    }

    /// <summary>对话框固定账号和需求版本，修改目标重新登记需求。</summary>
    private async Task EditTargetAsync(string uid, string name, MaterialDemand? demand)
    {
        var input = PromptDialog.Prompt(I18nService.Instance.Translate("目标为库存总量（非负整数）；修改目标会重新开始需求进度。"), name,
            (demand?.TargetQuantity ?? Rows.FirstOrDefault(row => row.Item.Name == name)?.Item.Quantity)?.ToString(CultureInfo.InvariantCulture) ?? "");
        if (string.IsNullOrEmpty(input) || Uid != uid) return;
        if (!long.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var target) || target < 0)
        {
            Status = I18nService.Instance.Translate("目标数量须为非负整数。");
            return;
        }
        await WriteAsync(uid, () => _store.SetDemandAsync(uid, name, target, demand?.Revision));
    }

    /// <summary>人工确认完成不写库存；二次明确确认避免误点。</summary>
    [RelayCommand]
    private async Task Confirm(AccountRecordsDemandRow? row)
    {
        if (!HasUid || row?.CanConfirm != true || !Rows.Contains(row)) return;
        var uid = Uid!;
        var revision = row.Demand!.Revision;
        if (await ThemedMessageBox.ShowAsync(string.Format(I18nService.Instance.Translate("确认“{0}”的需求已完成？此操作不修改库存数量。"), row.Item.Name),
                I18nService.Instance.Translate("人工确认达标"), MessageBoxButton.YesNo) != MessageBoxResult.Yes || Uid != uid) return;
        await WriteAsync(uid, () => _store.ConfirmDemandAsync(uid, row.Item.Name, revision));
    }

    /// <summary>归档当前已完成需求，保留完成证据与库存。</summary>
    [RelayCommand]
    private async Task Archive(AccountRecordsDemandRow? row)
    {
        if (!HasUid || row?.CanArchive != true || !Rows.Contains(row)) return;
        var uid = Uid!;
        var revisions = new Dictionary<string, string> { [row.Item.Name] = row.Demand!.Revision };
        await WriteAsync(uid, () => _store.ArchiveDemandsAsync(uid, revisions));
    }

    /// <summary>清理当前账号全部已完成需求，包括被筛选隐藏的行；存储层重新核实完成依据。</summary>
    [RelayCommand]
    private async Task ArchiveCompleted()
    {
        if (!HasUid || IsLoading) return;
        var uid = Uid!;
        var revisions = Rows.Where(row => row.CanArchive).ToDictionary(row => row.Item.Name, row => row.Demand!.Revision);
        if (revisions.Count == 0) return;
        await WriteAsync(uid, () => _store.ArchiveDemandsAsync(uid, revisions));
    }

    /// <summary>删除需求后保留库存物品行；无库存且无需求的行自然消失。</summary>
    [RelayCommand]
    private async Task Delete(AccountRecordsDemandRow? row)
    {
        if (!HasUid || row?.Demand == null || !Rows.Contains(row)) return;
        var uid = Uid!;
        var revision = row.Demand.Revision;
        if (await ThemedMessageBox.ShowAsync(string.Format(I18nService.Instance.Translate("删除“{0}”的需求？库存记录保留。"), row.Item.Name),
                I18nService.Instance.Translate("删除需求"), MessageBoxButton.YesNo) != MessageBoxResult.Yes || Uid != uid) return;
        await WriteAsync(uid, () => _store.DeleteDemandAsync(uid, row.Item.Name, revision));
    }

    /// <summary>沿用库存人工操作门禁，失败保留文件并展示原因。</summary>
    private async Task WriteAsync(string uid, Func<Task> action)
    {
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(action);
            if (Uid == uid) await ResumeAsync(uid);
        }
        catch (Exception ex) { if (Uid == uid) Status = AccountRecordsErrorMessages.Describe(ex); }
    }
}
