using System;
using System.Collections.ObjectModel;
using System.Linq;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 任务计划页头部的“CD 按账户”与“查看路线 CD”（统筹设计 5.6、9.4）。
/// 显示路线 CD 的账户默认按计划的运行账户选择：指定账户的计划为该账户，跟随当前账户的计划为本实例当前账户。
/// 账户目录有多个账户时可以临时改选其他账户，只影响本页的任务树标记、计划摘要和路线 CD 表单，不修改计划和实例设置；
/// 切换计划或离开本页后恢复按计划设置。
/// </summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>账户页视图模型；“查看路线 CD”在导航前记下要查看的账户和计划。测试直接构造时为空。</summary>
    private readonly PuloniaAccountsPageViewModel? _accountsPage;

    /// <summary>页面导航，用于跳转到账户页；测试直接构造时为空。</summary>
    private readonly INavigationService? _navigation;

    /// <summary>“CD 按账户”下拉框的选项：账户目录中的各账户，顺序与账户列表一致。</summary>
    public ObservableCollection<PuloniaCooldownFilterOption> CooldownAccountOptions { get; } = [];

    /// <summary>是否显示“CD 按账户”：选中了计划且账户目录有多个账户。</summary>
    public bool ShowCooldownAccountSelector => SelectedDocument is not null && CooldownAccountOptions.Count > 1;

    /// <summary>
    /// 本页当前按哪个账户显示路线 CD。改选其他账户时临时查看该账户；选回按计划设置选出的账户时恢复默认。
    /// </summary>
    public string? SelectedCooldownAccountId
    {
        get => SelectedDocument is { } document ? Session.GetCooldownAccount(document)?.Id : null;
        set
        {
            // 下拉框重建选项时会先写回 null，不能据此改变查看的账户
            if (value is null || SelectedDocument is not { } document || value == Session.GetCooldownAccount(document)?.Id)
                return;
            // 选回默认账户时传 null：之后计划的运行账户或本实例当前账户变化，显示随之更新
            Session.SetCooldownViewAccount(value == Session.GetDefaultCooldownAccount(document)?.Id ? null : value);
        }
    }

    /// <summary>临时查看其他账户时的说明，例如“仅改变本页显示，运行仍按“甲””；按计划设置显示时为 null。</summary>
    public string? CooldownAccountNotice
        => SelectedDocument is { } document && Session.GetCooldownAccount(document) is { } shown
           && Session.GetDefaultCooldownAccount(document) is { } planAccount && shown.Id != planAccount.Id
            ? $"仅改变本页显示，运行仍按“{planAccount.Name}”"
            : null;

    /// <summary>是否显示临时查看的说明。</summary>
    public bool HasCooldownAccountNotice => CooldownAccountNotice is not null;

    /// <summary>
    /// 跳到账户页查看路线 CD：选中本页当前显示 CD 的账户（含临时改选），详情切到“路线 CD”，并按当前计划筛选。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanShowRouteCooldowns))]
    private void ShowRouteCooldowns()
    {
        if (SelectedDocument is not { } document || _accountsPage is null || _navigation is null)
            return;
        // 先记下请求再导航：离开本页时临时选择会恢复，账户 ID 必须在导航前取得
        _accountsPage.RequestShowRouteCooldowns(Session.GetCooldownAccount(document)?.Id, document.Id);
        _navigation.Navigate(typeof(PuloniaAccountsPage));
    }

    /// <summary>选中了计划且能导航到账户页时可以查看路线 CD。</summary>
    private bool CanShowRouteCooldowns() => SelectedDocument is not null && _accountsPage is not null && _navigation is not null;

    /// <summary>会话重新读取账户与完成记录，或临时查看的账户变化后（在 UI 线程上）刷新选项与选中值。</summary>
    private void OnSessionCooldownsChanged(object? sender, EventArgs e) => RefreshCooldownAccountOptions();

    /// <summary>按会话最近读取的账户目录重建“CD 按账户”的选项；账户目录没变时不重建，避免下拉框闪动。</summary>
    private void RefreshCooldownAccountOptions()
    {
        var accounts = Session.CooldownAccountProfiles;
        if (!CooldownAccountOptions.Select(option => (option.Key, option.Label))
                .SequenceEqual(accounts.Select(account => ((string?)account.Id, account.Name))))
        {
            CooldownAccountOptions.Clear();
            foreach (var account in accounts)
                CooldownAccountOptions.Add(new PuloniaCooldownFilterOption(account.Id, account.Name));
        }
        NotifyCooldownAccountChanged();
    }

    /// <summary>恢复按计划设置显示路线 CD；没有临时选择时不重新计算标记。</summary>
    private void ResetCooldownViewAccount()
    {
        if (Session.CooldownViewAccountId is not null)
            Session.SetCooldownViewAccount(null);
    }

    /// <summary>计划切换、运行账户变化或会话重新读取后同步“CD 按账户”的显示。</summary>
    private void NotifyCooldownAccountChanged()
    {
        OnPropertyChanged(nameof(ShowCooldownAccountSelector));
        OnPropertyChanged(nameof(SelectedCooldownAccountId));
        OnPropertyChanged(nameof(CooldownAccountNotice));
        OnPropertyChanged(nameof(HasCooldownAccountNotice));
    }
}
