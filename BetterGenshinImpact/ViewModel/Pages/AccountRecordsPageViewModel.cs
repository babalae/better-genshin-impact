using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.ViewModel.Controls;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 管理账号记录分页、账户选择与库存服务；其他分页暂保留占位。
/// UID 列表来自 Pulonia 账户目录中填写了 UID 的账户，新增、修改 UID 与删除账户都在 Pulonia 账户页进行。
/// </summary>
public partial class AccountRecordsPageViewModel : ViewModel
{
    /// <summary>账号持久化服务。</summary>
    private readonly InventoryStore _store;
    /// <summary>Pulonia 账户服务：提供账户目录与本实例当前账户，导入时为不在目录中的 UID 新建账户。</summary>
    private readonly IPuloniaAccountService _accounts;
    /// <summary>页面导航，用于“管理账户”跳转到账户页；测试直接构造时为空。</summary>
    private readonly INavigationService? _navigation;
    /// <summary>页面所在的 UI 线程；账户服务的事件可能来自后台线程。</summary>
    private readonly Dispatcher _dispatcher;
    /// <summary>页面是否正在显示；只在显示期间响应账户目录变化，隐藏时由下次进入重新读取。</summary>
    private bool _isActive;
    /// <summary>是否已经成功读取过账户目录；读取前不显示“没有账户”的提示。</summary>
    private bool _accountsLoaded;
    /// <summary>账户页“打开狛荷屋”或导入完成后要选中的 UID，下次加载时应用；没有请求时为 null。</summary>
    private string? _requestedUid;
    /// <summary>库存子分页。</summary>
    public AccountRecordsInventoryViewModel Inventory { get; }

    /// <summary>所有分页共享的列宽恢复信号，不影响排序和库存内容。</summary>
    [ObservableProperty] private int _columnWidthResetVersion;

    /// <summary>通知各分页恢复 XAML 中定义的初始列宽。</summary>
    [RelayCommand]
    private void ResetColumnWidths() => ColumnWidthResetVersion++;

    /// <summary>
    /// 两次确认后重置全部账户的狛荷屋数据和设置，并同步页面的筛选与显示偏好。
    /// UID 列表来自账户目录，不受重置影响；所选账户保持不变，重新读取后显示为空库存。
    /// </summary>
    [RelayCommand]
    private async Task ResetToDefaults()
    {
        if (await ThemedMessageBox.ShowAsync("将清空所有账户的狛荷屋库存、异常、事件和快照，并把补充字典、来源字典、Warning 规则及狛荷屋设置恢复为预设值。账户目录不受影响。是否继续？",
                "重置为预设值（1/2）", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning) != MessageBoxResult.Yes) return;
        if (await ThemedMessageBox.ShowAsync("再次确认：此操作影响所有账户，覆盖全部自定义字典和狛荷屋配置。原数据会保留在 AccountRecords 同级备份目录，但界面不提供撤销。确定执行重置？",
                "最终确认（2/2）", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.ResetToDefaultsAsync());
            IsUidDropDownOpen = false;
            Array.Fill(_searchTexts, "");
            SelectedTabIndex = 0;
            OnPropertyChanged(nameof(SearchText));
            Inventory.SearchText = "";
            Inventory.AllCategoriesSelected = true;
            ResetColumnWidths();
            // 先清空库存显示，Load 随之重新读取偏好并完整读取所选账户
            await Inventory.SelectUidAsync(null);
            await Load();
            await ThemedMessageBox.ShowAsync("已重置为预设值，所有账户的狛荷屋记录已清空。", "重置完成", MessageBoxButton.OK);
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(AccountRecordsErrorMessages.Describe(ex), "重置失败", MessageBoxButton.OK, ThemedMessageBox.MessageBoxIcon.Error); }
    }

    /// <summary>注入公共存储、独立的库存分页模型与 Pulonia 账户服务，并订阅账户目录变化。</summary>
    /// <param name="store">狛荷屋库存存储</param>
    /// <param name="inventory">库存子分页</param>
    /// <param name="accounts">Pulonia 账户服务</param>
    /// <param name="navigation">页面导航；测试直接构造时为空</param>
    public AccountRecordsPageViewModel(InventoryStore store, AccountRecordsInventoryViewModel inventory, IPuloniaAccountService accounts,
        INavigationService? navigation = null)
    {
        _store = store; Inventory = inventory;
        _accounts = accounts;
        _navigation = navigation;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        // 页面视图模型为单例，订阅与应用同寿命，不需要退订
        _accounts.AccountsChanged += OnAccountsChanged;
    }
    /// <summary>各分页独立保留搜索词；库存已接入过滤，其余分页等待实现。</summary>
    private readonly string[] _searchTexts = ["", "", ""];

    /// <summary>账户目录中填写了 UID 的账户，顺序与账户列表一致；选中值为 UID。</summary>
    public ObservableCollection<AccountRecordsAccountOption> UidOptions { get; } = [];

    /// <summary>账户目录已读取，但没有任何填写 UID 的账户；此时提示到账户页填写 UID。</summary>
    public bool HasNoAccounts => _accountsLoaded && UidOptions.Count == 0;

    /// <summary>当前分页索引。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTabTitle))]
    [NotifyPropertyChangedFor(nameof(SearchPlaceholder))]
    [NotifyPropertyChangedFor(nameof(SearchText))]
    private int _selectedTabIndex;

    /// <summary>当前选择的 UID；尚无账户时保持空白。</summary>
    [ObservableProperty]
    private string? _selectedUid;

    /// <summary>UID 下拉列表的展开状态，由标准选择控件维护。</summary>
    [ObservableProperty]
    private bool _isUidDropDownOpen;

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

    /// <summary>
    /// 记下要选中的 UID，供账户页“打开狛荷屋”在导航前调用；页面加载时应用，UID 不在账户目录中时按默认选择。
    /// </summary>
    /// <param name="uid">要选中的完整 UID</param>
    public void RequestSelectUid(string uid) => _requestedUid = uid;

    /// <summary>页面进入时按账户目录同步 UID 列表并选中账户，在数据未变时复用当前库存快照。</summary>
    [RelayCommand]
    private async Task Load()
    {
        _isActive = true;
        try
        {
            if (Inventory.Uid == null) await Inventory.LoadPreferencesAsync();
            // 1. 读取账户目录：只列出填写了狛荷屋可用 UID 的账户（首位为 0 的 UID 不能作为数据目录），顺序与账户列表一致
            var settings = await _accounts.LoadAccountsAsync();
            var options = settings.Accounts.Where(account => account.Uid is { } uid && InventoryStore.IsValidUid(uid))
                .Select(account => new AccountRecordsAccountOption(account.Id, account.Name, account.Uid!)).ToArray();
            // 2. 同步前记下所选账户：同步时下拉框可能把选中值清空，账户 UID 修改后仍要选中同一账户
            var selectedAccountId = UidOptions.FirstOrDefault(option => option.Uid == SelectedUid)?.AccountId;
            var selectedUid = SelectedUid;
            SyncOptions(options);
            _accountsLoaded = true;
            OnPropertyChanged(nameof(HasNoAccounts));
            // 3. 选中请求的 UID、原来的账户或默认账户；未变化时只检查库存文件是否更新
            var target = await ChooseUidAsync(options, selectedAccountId, selectedUid);
            _requestedUid = null;
            if (SelectedUid != target) SelectedUid = target;
            else await Inventory.ResumeAsync(SelectedUid);
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(AccountRecordsErrorMessages.Describe(ex), "读取狛荷屋记录", MessageBoxButton.OK); }
    }

    /// <summary>
    /// 按账户目录原位更新下拉框选项：删除已不在目录中的项，按目录顺序插入新项或移动已有项，名称变化时原位改名。
    /// 未变化时不触碰集合，避免下拉框清空选中值引起库存重新加载。
    /// </summary>
    /// <param name="options">账户目录中可用于狛荷屋的账户，顺序与账户列表一致</param>
    private void SyncOptions(IReadOnlyList<AccountRecordsAccountOption> options)
    {
        // 1. 删除已不在目录中的账户，或 UID 已改由其他账户使用的旧项
        for (var index = UidOptions.Count - 1; index >= 0; index--)
            if (!options.Any(option => option.Uid == UidOptions[index].Uid && option.AccountId == UidOptions[index].AccountId))
                UidOptions.RemoveAt(index);
        // 2. 按目录顺序插入新账户、移动已有账户，并同步名称
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var current = UidOptions.Select((item, position) => (item, position))
                .FirstOrDefault(pair => pair.item.Uid == option.Uid && pair.item.AccountId == option.AccountId);
            if (current.item == null)
            {
                UidOptions.Insert(index, option);
                continue;
            }
            if (current.position != index) UidOptions.Move(current.position, index);
            current.item.Name = option.Name;
        }
    }

    /// <summary>
    /// 决定要选中的 UID：账户页请求的 UID → 原来选中的账户（UID 修改后取新 UID）→ 原来的 UID → 本实例当前账户 → 第一个账户。
    /// </summary>
    /// <param name="options">账户目录中可用于狛荷屋的账户</param>
    /// <param name="selectedAccountId">同步前选中的账户 ID</param>
    /// <param name="selectedUid">同步前选中的 UID</param>
    /// <returns>要选中的 UID；没有可用账户时为 null</returns>
    private async Task<string?> ChooseUidAsync(IReadOnlyList<AccountRecordsAccountOption> options, string? selectedAccountId, string? selectedUid)
    {
        if (options.Count == 0) return null;
        if (_requestedUid is { } requested && options.Any(option => option.Uid == requested)) return requested;
        if (options.FirstOrDefault(option => option.AccountId == selectedAccountId) is { } sameAccount) return sameAccount.Uid;
        if (selectedUid != null && options.Any(option => option.Uid == selectedUid)) return selectedUid;
        // 首次打开或所选账户已不在目录中：默认选中本实例当前账户，它没有 UID 时选第一个有 UID 的账户
        var currentAccountId = await _accounts.GetCurrentAccountIdAsync(_accounts.CurrentInstanceKey);
        return (options.FirstOrDefault(option => option.AccountId == currentAccountId) ?? options[0]).Uid;
    }

    /// <summary>账户目录在本进程保存后，页面正在显示时重新读取 UID 列表；隐藏时由下次进入读取。</summary>
    private void OnAccountsChanged(object? sender, EventArgs e)
        => _dispatcher.InvokeAsync(() =>
        {
            if (_isActive) _ = Load();
        });

    /// <summary>关闭 UID 下拉框并跳转到 Pulonia 账户页；新增账户、修改 UID 和删除账户都在账户页进行。</summary>
    [RelayCommand]
    private void ManageAccounts()
    {
        IsUidDropDownOpen = false;
        _navigation?.Navigate(typeof(PuloniaAccountsPage));
    }

    /// <summary>保留蓝色按钮的开发占位入口，与文件导入导出分开。</summary>
    [RelayCommand]
    private void OpenPlaceholder() => new AccountRecordsPlaceholderDialog
    {
        Owner = Application.Current.MainWindow
    }.ShowDialog();

    /// <summary>从菜单导入账户库存；目标 UID 不在账户目录中时确认后先建账户，关闭后同步 UID 列表和库存结果。</summary>
    [RelayCommand]
    private async Task OpenImport()
    {
        var import = new AccountRecordsImportViewModel(_store, EnsureImportAccountAsync);
        var dialog = new AccountRecordsImportDialog(import)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
        await RefreshAfterImportAsync(import.ImportedUid);
        if (import.CompletionSummary != null && !import.HasImportErrors)
        {
            _ = Inventory.ShowImportSummaryAsync(import.CompletionSummary);
        }
    }

    /// <summary>
    /// 导入前确认目标 UID 在账户目录中：不在时询问“创建账户“账户 尾号 xxxx”并导入？”，确认后新建账户
    /// （名称重复时加序号，服务器时区与账户页新增账户的默认值一致）。在人工操作门禁外调用，避免与账户保存后的数据改名互相等待。
    /// </summary>
    /// <param name="uid">导入目标的完整 UID</param>
    /// <returns>账户已存在或已创建时为 true；用户取消时为 false</returns>
    internal async Task<bool> EnsureImportAccountAsync(string uid)
    {
        // 1. 账户目录中已有该 UID 时直接导入
        var settings = await _accounts.LoadAccountsAsync();
        if (settings.Accounts.Any(account => account.Uid == uid)) return true;
        // 2. 按“账户 尾号 xxxx”命名，确认后新建并保存账户目录
        var name = UniqueAccountName("账户 尾号 " + uid[^4..], settings.Accounts.Select(account => account.Name.Trim()));
        if (await ThemedMessageBox.ShowAsync($"UID {uid} 不在账户目录中。\n\n创建账户“{name}”并导入？", "导入库存",
                MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return false;
        settings.Accounts.Add(new PuloniaGenshinAccountProfile
        {
            Name = name, Uid = uid, ServerTimeZone = PuloniaServerTime.GetDefaultServerTimeZone()
        });
        await _accounts.SaveAccountsAsync(settings, new Dictionary<string, (string UserName, string Password)>());
        return true;
    }

    /// <summary>名称与已有账户重复时在后面加序号，例如“账户 尾号 5678（2）”。</summary>
    /// <param name="name">首选名称</param>
    /// <param name="existing">已有账户名称</param>
    private static string UniqueAccountName(string name, IEnumerable<string> existing)
    {
        var names = existing.ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(name)) return name;
        for (var index = 2; ; index++)
        {
            var candidate = $"{name}（{index}）";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    /// <summary>导入可能部分成功；无论是否有异常，均完整读取目标账户，淘汰导入前尚在加载的快照。</summary>
    internal async Task RefreshAfterImportAsync(string? importedUid)
    {
        // 导入时可能刚为目标 UID 新建账户，重新读取账户目录后选中它
        if (importedUid != null) _requestedUid = importedUid;
        await Load();
        if (importedUid == null) return;
        // 同一 UID 赋值不会触发属性回调，ResumeAsync 也可能因正在加载而跳过，必须显式刷新。
        await Inventory.SelectUidAsync(SelectedUid);
    }

    /// <summary>从标题菜单导出所选 UID 的当前库存；完整写入临时文件后替换目标。</summary>
    [RelayCommand]
    private async Task ExportInventory()
    {
        var uid = SelectedUid;
        if (uid == null)
        {
            await ThemedMessageBox.ShowAsync("尚未选择账户。", "导出库存", MessageBoxButton.OK);
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出当前库存", Filter = "狛荷屋库存 JSON|*.json", DefaultExt = ".json", AddExtension = true,
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
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(AccountRecordsErrorMessages.Describe(ex), "导出失败", MessageBoxButton.OK); }
        finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>清空所选账户的库存记录；删除账户及其全部狛荷屋记录改在 Pulonia 账户页进行。</summary>
    /// <param name="scope">清除范围；只支持 inventory（清空库存、异常、事件和库存快照，保留数据目录）</param>
    [RelayCommand]
    private async Task ClearAccount(string scope)
    {
        if (scope != "inventory") return;
        var uid = SelectedUid;
        if (uid == null) return;
        var name = UidOptions.FirstOrDefault(option => option.Uid == uid)?.DisplayText ?? uid;
        if (await ThemedMessageBox.ShowAsync($"确定清空账户“{name}”的库存、异常、事件和库存快照？此操作不可撤销。", "清空库存记录",
                MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await AccountRecordsInteractionGate.Instance.ExecuteManualAsync(() => _store.ClearAsync(uid, false, false));
            await Load();
        }
        catch (Exception ex) { await ThemedMessageBox.ShowAsync(AccountRecordsErrorMessages.Describe(ex), "清空库存记录失败", MessageBoxButton.OK); }
    }

    /// <summary>页面离开时关闭下拉列表，并停止响应账户目录变化。</summary>
    [RelayCommand]
    private void CloseUidPopups()
    {
        IsUidDropDownOpen = false;
        _isActive = false;
    }
}
