using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.ViewModel.Controls;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>管理账号记录分页、账号选择与库存服务；其他分页暂保留占位。</summary>
public partial class AccountRecordsPageViewModel : ViewModel
{
    /// <summary>账号持久化服务。</summary>
    private readonly InventoryStore _store;
    /// <summary>库存子分页。</summary>
    public AccountRecordsInventoryViewModel Inventory { get; }

    /// <summary>所有分页共享的列宽恢复信号，不影响排序和库存内容。</summary>
    [ObservableProperty] private int _columnWidthResetVersion;

    /// <summary>通知各分页恢复 XAML 中定义的初始列宽。</summary>
    [RelayCommand]
    private void ResetColumnWidths() => ColumnWidthResetVersion++;

    /// <summary>注入公共存储与独立的库存分页模型。</summary>
    public AccountRecordsPageViewModel(InventoryStore store, AccountRecordsInventoryViewModel inventory)
    {
        _store = store; Inventory = inventory;
    }
    /// <summary>各分页独立保留的搜索词，待对应列表接入后用于过滤。</summary>
    private readonly string[] _searchTexts = ["", "", ""];

    /// <summary>已保存的 UID；新增操作独立于此集合。</summary>
    public ObservableCollection<string> UidOptions { get; } = [];

    /// <summary>当前分页索引。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTabTitle))]
    [NotifyPropertyChangedFor(nameof(SearchPlaceholder))]
    [NotifyPropertyChangedFor(nameof(SearchText))]
    private int _selectedTabIndex;

    /// <summary>当前选择的 UID；尚无账号时保持空白。</summary>
    [ObservableProperty]
    private string? _selectedUid;

    /// <summary>UID 下拉列表的展开状态，由标准选择控件维护。</summary>
    [ObservableProperty]
    private bool _isUidDropDownOpen;

    /// <summary>是否展开新增 UID 输入区。</summary>
    [ObservableProperty]
    private bool _isAddingUid;

    /// <summary>等待添加的 UID。</summary>
    [ObservableProperty]
    private string _pendingUid = "";

    /// <summary>新增 UID 时的输入提示。</summary>
    [ObservableProperty]
    private string _uidValidationMessage = "";

    /// <summary>当前分页名称，用于搜索框提示。</summary>
    public string CurrentTabTitle => SelectedTabIndex switch
    {
        1 => "材料需求",
        2 => "活动日历",
        _ => "库存记录"
    };

    /// <summary>搜索框提示当前搜索的分页。</summary>
    public string SearchPlaceholder => $"搜索{CurrentTabTitle}";

    /// <summary>当前分页的搜索词，不改变其他分页的输入。</summary>
    public string SearchText
    {
        get => _searchTexts[Math.Clamp(SelectedTabIndex, 0, _searchTexts.Length - 1)];
        set
        {
            if (SetProperty(ref _searchTexts[Math.Clamp(SelectedTabIndex, 0, _searchTexts.Length - 1)], value) && SelectedTabIndex == 0)
                Inventory.SearchText = value;
        }
    }

    /// <summary>账号切换由子模型保证异步结果不会串号。</summary>
    partial void OnSelectedUidChanged(string? value) => _ = Inventory.SelectUidAsync(value);

    /// <summary>页面进入时同步磁盘账号列表和当前库存。</summary>
    [RelayCommand]
    private async Task Load()
    {
        try
        {
            await Inventory.LoadPreferencesAsync();
            var uids = await _store.ListUidsAsync();
            foreach (var uid in UidOptions.Except(uids).ToArray()) UidOptions.Remove(uid);
            foreach (var uid in uids.Except(UidOptions)) UidOptions.Add(uid);
            if (SelectedUid == null || !UidOptions.Contains(SelectedUid)) SelectedUid = uids.FirstOrDefault();
            else await Inventory.SelectUidAsync(SelectedUid);
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(ex.Message, "读取账号记录", MessageBoxButton.OK); }
    }

    /// <summary>关闭 UID 列表并进入新增浮窗。</summary>
    [RelayCommand]
    private void BeginAddUid()
    {
        IsUidDropDownOpen = false;
        PendingUid = "";
        UidValidationMessage = "";
        IsAddingUid = true;
    }

    /// <summary>保留蓝色按钮的开发占位入口，与文件导入导出分开。</summary>
    [RelayCommand]
    private void OpenPlaceholder() => new AccountRecordsPlaceholderDialog
    {
        Owner = Application.Current.MainWindow
    }.ShowDialog();

    /// <summary>从菜单导入账号库存；关闭后同步新增 UID 和库存结果。</summary>
    [RelayCommand]
    private async Task OpenImport()
    {
        var import = new AccountRecordsImportViewModel(_store);
        var dialog = new AccountRecordsImportDialog(import)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
        await Load();
        if (import.ImportedUid != null && UidOptions.Contains(import.ImportedUid)) SelectedUid = import.ImportedUid;
        if (import.CompletionSummary != null && !import.HasImportErrors)
        {
            await Inventory.SelectUidAsync(SelectedUid);
            _ = Inventory.ShowImportSummaryAsync(import.CompletionSummary);
        }
    }

    /// <summary>从标题菜单导出所选 UID 的当前库存；完整写入临时文件后替换目标。</summary>
    [RelayCommand]
    private async Task ExportInventory()
    {
        var uid = SelectedUid;
        if (uid == null)
        {
            await ThemedMessageBox.ShowAsync("请先选择一个 UID。", "导出库存", MessageBoxButton.OK);
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出当前库存", Filter = "账号记录 JSON|*.json", DefaultExt = ".json", AddExtension = true,
            FileName = $"AccountRecords-{uid}-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };
        if (dialog.ShowDialog() != true) return;
        string? temporary = null;
        try
        {
            var package = await _store.ExportInventoryAsync(uid);
            temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, package.ToString(Newtonsoft.Json.Formatting.Indented), new UTF8Encoding(false));
            File.Move(temporary, dialog.FileName, true);
            await ThemedMessageBox.ShowAsync("当前库存已导出。文件保留原记录时间与来源，不包含异常、快照或图标。", "导出完成", MessageBoxButton.OK);
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(ex.Message, "导出失败", MessageBoxButton.OK); }
        finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>保存空账号；重复输入时直接选择已有项。</summary>
    [RelayCommand]
    private async Task AddUid()
    {
        var uid = PendingUid.Trim();
        if (uid.Length is 0 or > 20 || !uid.All(char.IsAsciiDigit))
        {
            UidValidationMessage = "请输入仅包含数字的 UID。";
            return;
        }

        try
        {
            await _store.AddUidAsync(uid);
            if (!UidOptions.Contains(uid)) UidOptions.Add(uid);
            SelectedUid = uid;
            CancelAddUid();
        }
        catch (Exception ex) { UidValidationMessage = ex.Message; }
    }

    /// <summary>区分清空库存和连同 UID 一并删除全部记录。</summary>
    [RelayCommand]
    private async Task ClearAccount(string scope)
    {
        if (scope is not ("inventory" or "uid")) return;
        var uid = SelectedUid;
        if (uid == null) return;
        var description = scope switch
        {
            "uid" => "删除该 UID 及其全部账号记录和快照",
            _ => "清空该 UID 的库存、异常和库存快照，但保留 UID"
        };
        if (await ThemedMessageBox.ShowAsync($"确定{description}？此操作不可撤销。", "清除账号记录", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _store.ClearAsync(uid, false, scope == "uid");
            await Load();
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(ex.Message, "清除账号记录失败", MessageBoxButton.OK); }
    }

    /// <summary>关闭新增输入区，不修改已有 UID。</summary>
    [RelayCommand]
    private void CancelAddUid()
    {
        IsAddingUid = false;
        PendingUid = "";
        UidValidationMessage = "";
    }

    /// <summary>页面离开时关闭下拉列表和新增浮窗。</summary>
    [RelayCommand]
    private void CloseUidPopups()
    {
        IsUidDropDownOpen = false;
        CancelAddUid();
    }
}
