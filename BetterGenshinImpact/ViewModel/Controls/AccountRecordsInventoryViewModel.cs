using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
    /// <summary>补齐完整字典分类，保留用户已有选择。</summary>
    private void UpdateCategories(System.Collections.Generic.IEnumerable<string> categories)
    {
        foreach (var name in categories.Select(value => string.IsNullOrEmpty(value) ? "未分类" : value).Append("未分类").Distinct().OrderBy(value => value))
            if (!Categories.Any(category => category.Name == name)) Categories.Add(new AccountRecordsCategoryOption(name, CategoriesChanged));
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
        var categories = await Task.Run(() => _catalog.Items.Values.ToArray());
        UpdateCategories(categories);
        OnPropertyChanged(nameof(PreferReadableSource)); OnPropertyChanged(nameof(SnapshotsEnabled));
    }

    /// <summary>切换 UID 并丢弃过期异步读取结果。</summary>
    public async Task SelectUidAsync(string? uid)
    {
        var changedUid = Uid != uid;
        Uid = uid; OnPropertyChanged(nameof(HasUid));
        var version = ++_loadVersion;
        if (changedUid) { Rows.Clear(); OnPropertyChanged(nameof(IsEmpty)); }
        Status = "";
        if (uid == null) return;
        try
        {
            var workspace = await _store.LoadAsync(uid);
            var rows = await Task.Run(() => workspace.Inventory.Items.Keys.Union(workspace.Inventory.Errors.Keys, StringComparer.Ordinal).Select(name =>
            {
                workspace.Inventory.Items.TryGetValue(name, out var recent);
                workspace.Inventory.Errors.TryGetValue(name, out var error);
                var current = recent?.FirstOrDefault();
                var reference = current?.Source ?? error?.Source ?? "";
                workspace.Sources.TryGetValue(reference, out var source);
                workspace.Definitions.TryGetValue(name, out var category);
                return new AccountRecordsInventoryRow
                {
                    Name = name, Category = string.IsNullOrEmpty(category) ? "未分类" : category,
                    Quantity = current?.Quantity, RecordedAt = current?.RecordedAt,
                    SourceReference = reference,
                    SourceName = workspace.Settings.PreferReadableSource && !string.IsNullOrWhiteSpace(source?.DisplayName) ? source.DisplayName : reference,
                    Icon = _icons.Load(name), Error = error, Recent = recent ?? []
                };
            }).ToArray());
            if (version != _loadVersion) return;
            _workspace = workspace;
            UpdateCategories(workspace.Definitions.Values);
            Rows.Clear();
            // 集合变更会让 DataGrid 同步查询视图；不能在 DeferRefresh 期间逐项添加。
            foreach (var row in rows) Rows.Add(row);
            Status = workspace.Inventory.Errors.Count > 0
                ? $"{workspace.Inventory.Errors.Count} 项库存异常待处理，请点击对应行的红灯查看，或右键手动修改。" : "";
            OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(PreferReadableSource)); OnPropertyChanged(nameof(SnapshotsEnabled));
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
                Status = $"库存读取失败：{ex.Message}" + (Rows.Count > 0 ? " 当前保留上次读取的内容，请刷新重试。" : " 请刷新重试。");
        }
    }

    /// <summary>手动刷新当前账号。</summary>
    [RelayCommand] private Task Refresh() => SelectUidAsync(Uid);

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

    /// <summary>显示错误证据，允许接受冲突或舍弃；格式错误引导右键修订。</summary>
    [RelayCommand] private async Task ShowError(AccountRecordsInventoryRow? row)
    {
        if (Uid == null || row?.Error == null) return;
        var uid = Uid;
        var error = row.Error;
        var current = error.Current == null ? "无有效记录" : $"{error.Current.Quantity} / {error.Current.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} / {error.Current.Source}";
        var incoming = error.Code == "ReadFailed" ? "读取失败，没有提交数量" : $"{error.Incoming.Quantity} / {error.Incoming.RecordedAt ?? "本次采集"}";
        var detail = $"{row.Name}\n{row.ErrorText}\n\n原记录：{current}\n传入：{incoming}\n来源：{error.Source}\n发生时间：{error.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        if (error.CanAccept)
        {
            var result = await ThemedMessageBox.ShowAsync(detail + "\n\n是：接受传入记录；否：舍弃；取消：保持待处理。", "库存记录冲突", MessageBoxButton.YesNoCancel, ThemedMessageBox.MessageBoxIcon.Warning);
            if (result is MessageBoxResult.Yes or MessageBoxResult.No) await Run(() => _store.ResolveAsync(uid, row.Name, error.Id, result == MessageBoxResult.Yes));
        }
        else
        {
            var result = await ThemedMessageBox.ShowAsync(detail + "\n\n是否舍弃本次异常上报？选择“否”后可右键手动修改。", "库存记录异常", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning);
            if (result == MessageBoxResult.Yes) await Run(() => _store.ResolveAsync(uid, row.Name, error.Id, false));
        }
    }

    /// <summary>展示各来源最后一次上报，不解释为获得量历史。</summary>
    [RelayCommand] private Task ShowRecent(AccountRecordsInventoryRow? row) => row == null ? Task.CompletedTask :
        ShowRecentCore(row);

    /// <summary>完整展示来源路径与本地时间。</summary>
    private static async Task ShowRecentCore(AccountRecordsInventoryRow row)
    {
        await ThemedMessageBox.ShowAsync(
            row.Recent.Count == 0 ? "尚无有效库存记录。" : string.Join("\n\n", row.Recent.Select(record => $"数量：{record.Quantity}\n时间：{record.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n来源：{record.Source}")),
            $"{row.Name} · 最近来源记录", MessageBoxButton.OK);
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
        var value = PromptDialog.Prompt(snapshots ? "保留最近几个有记录的日期？（1～30）" : "每个物品保留几个最近来源？（1～10，含当前记录）", "保存设置",
            (snapshots ? _workspace.Settings.SnapshotLimit : _workspace.Settings.RecentLimit).ToString(CultureInfo.InvariantCulture));
        if (string.IsNullOrEmpty(value)) return;
        if (!int.TryParse(value, out var count) || count < 1 || count > (snapshots ? 30 : 10))
        { await ThemedMessageBox.ShowAsync("请输入范围内的整数。", "保存设置", MessageBoxButton.OK); return; }
        await Run(async () =>
        {
            if (snapshots) _workspace.Settings.SnapshotLimit = count; else _workspace.Settings.RecentLimit = count;
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
