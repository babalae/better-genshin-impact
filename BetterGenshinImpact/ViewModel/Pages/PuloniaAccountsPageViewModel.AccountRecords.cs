using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.View.Pages;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 账户页详情的“狛荷屋”页签（统筹设计 9.3）：跳转到狛荷屋页面并选中所选账户的 UID。
/// 这是界面层导航：账户页依赖狛荷屋页视图模型与页面导航，Pulonia 服务层不依赖狛荷屋。
/// 账户没有 UID、UID 草稿尚未保存或 UID 不能用于狛荷屋时按钮不可用，并说明原因。
/// </summary>
public partial class PuloniaAccountsPageViewModel
{
    /// <summary>详情页签“狛荷屋”的序号，排在“首领领奖”之后。</summary>
    internal const int AccountRecordsTabIndex = 4;

    /// <summary>狛荷屋页视图模型；“打开狛荷屋”在导航前记下要选中的 UID。测试直接构造时为空。</summary>
    private readonly AccountRecordsPageViewModel? _accountRecordsPage;

    /// <summary>页面导航，用于跳转到狛荷屋页；测试直接构造时为空。</summary>
    private readonly INavigationService? _navigation;

    /// <summary>
    /// “狛荷屋”页签的说明：没有 UID、UID 未保存或不能用于狛荷屋时提示先处理；否则说明会打开哪个 UID 的记录。
    /// </summary>
    public string AccountRecordsHint
    {
        get
        {
            if (SelectedAccount is not { } account)
                return "";
            // 1. 跳转按已保存的 UID 进行：没有 UID 的账户不出现在狛荷屋
            if (string.IsNullOrEmpty(account.Saved?.Uid))
                return "这个账户还没有保存 UID，不会出现在狛荷屋。请先在“概况”中填写 UID 并保存。";
            // 2. UID 草稿与已保存的不同时，狛荷屋仍显示旧 UID，先保存
            if (account.Uid.Trim() != account.Saved.Uid)
                return "UID 有未保存的修改，请先保存，再打开狛荷屋。";
            // 3. 狛荷屋按 UID 命名数据目录，首位为 0 的 UID 不能使用
            if (!InventoryStore.IsValidUid(account.Saved.Uid))
                return $"UID {account.Saved.Uid} 首位为 0，狛荷屋不支持这个 UID。";
            return $"打开狛荷屋，查看 UID {account.Saved.Uid} 的库存记录。";
        }
    }

    /// <summary>能否打开狛荷屋：所选账户已保存可用的 UID、草稿未修改 UID，且能导航到狛荷屋页。</summary>
    private bool CanOpenAccountRecords() => CanEdit && _accountRecordsPage is not null && _navigation is not null
                                            && SelectedAccount is { Saved.Uid: { Length: > 0 } uid } account
                                            && account.Uid.Trim() == uid && InventoryStore.IsValidUid(uid);

    /// <summary>跳到狛荷屋页并选中所选账户的 UID；先记下请求再导航，狛荷屋页加载时应用。</summary>
    [RelayCommand(CanExecute = nameof(CanOpenAccountRecords))]
    private void OpenAccountRecords()
    {
        if (!CanOpenAccountRecords())
            return;
        _accountRecordsPage!.RequestSelectUid(SelectedAccount!.Saved!.Uid!);
        _navigation!.Navigate(typeof(AccountRecordsPage));
    }

    /// <summary>所选账户、UID 草稿或保存状态变化后更新“打开狛荷屋”与说明。</summary>
    private void NotifyAccountRecordsCommands()
    {
        OpenAccountRecordsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(AccountRecordsHint));
    }
}
