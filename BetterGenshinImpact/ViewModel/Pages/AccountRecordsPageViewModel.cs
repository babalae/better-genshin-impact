using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>管理账号记录页面的分页、搜索和临时 UID 选择，不接入业务存储。</summary>
public partial class AccountRecordsPageViewModel : ViewModel
{
    /// <summary>各分页独立保留的搜索词，待对应列表接入后用于过滤。</summary>
    private readonly string[] _searchTexts = ["", "", ""];

    /// <summary>当前会话中的真实 UID；新增操作独立于此集合。</summary>
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

    /// <summary>筛选入口的预留开关，当前仅改变显示状态，不过滤数据。</summary>
    [ObservableProperty]
    private bool _isFilterEnabled;

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
        set => SetProperty(ref _searchTexts[Math.Clamp(SelectedTabIndex, 0, _searchTexts.Length - 1)], value);
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

    /// <summary>显示统一导入占位弹窗，后续接入 JSON 或插件导入。</summary>
    [RelayCommand]
    private void OpenImport()
    {
        var dialog = new AccountRecordsImportDialog()
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    /// <summary>将数字 UID 加入当前会话；重复输入时直接选择已有项。</summary>
    [RelayCommand]
    private void AddUid()
    {
        var uid = PendingUid.Trim();
        if (uid.Length == 0 || !uid.All(char.IsAsciiDigit))
        {
            UidValidationMessage = "请输入仅包含数字的 UID。";
            return;
        }

        if (!UidOptions.Contains(uid))
        {
            UidOptions.Add(uid);
        }

        SelectedUid = uid;
        CancelAddUid();
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
