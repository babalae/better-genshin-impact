using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 一条龙的运行账户设置，始终编辑同一计划的 <see cref="PuloniaTaskPlan.AccountId"/>：
/// 跟随当前账户（默认，不切换）或指定账户（运行前切换并核验 UID）。指定账户时清单首位显示“切换账户”准备项。
/// 入口规则与文案和任务计划页共用 <see cref="PuloniaRunAccountText"/>。
/// </summary>
public partial class PuloniaOneDragonViewModel
{
    /// <summary>当前已保存的账户目录，用于显示名称。</summary>
    public ObservableCollection<PuloniaGenshinAccountProfile> GenshinAccounts { get; } = [];
    /// <summary>运行账户下拉框的选项：“跟随当前账户”加填写了 UID 的账户；键为账户 ID，跟随当前账户为空字符串。</summary>
    public ObservableCollection<KeyValuePair<string, string>> RunAccountOptions { get; } = [];
    /// <summary>是否正在查看固定首位的系统准备项。</summary>
    [ObservableProperty] private bool _isAccountPreparationSelected;
    /// <summary>运行账户保存或读取的反馈。</summary>
    [ObservableProperty] private string _accountMessage = "";
    /// <summary>账户页视图模型；“查看路线 CD”在导航前记下要查看的账户和配置。测试直接构造时为空。</summary>
    private readonly PuloniaAccountsPageViewModel? _accountsPage;

    /// <summary>当前配置的运行账户设置：空字符串表示跟随当前账户，否则为指定账户 ID。</summary>
    public string SelectedGenshinAccountId
    {
        get => SelectedConfiguration?.Plan.AccountId ?? PuloniaRunAccountText.FollowCurrentKey;
        set
        {
            // 下拉框重建选项时会先写回 null，不能据此把指定账户改成跟随当前账户。
            if (value is null || SelectedConfiguration is not { } document) return;
            var accountId = value.Length == 0 ? null : value;
            if (accountId == document.Plan.AccountId) return;
            // 只能指定填写了 UID 的账户，因为切换和核验都要按 UID 找到目标。
            if (accountId is not null && !PuloniaRunAccountText.CanSpecify(accountId, GenshinAccounts))
                return;
            document.ApplyMutation(() => document.Plan.AccountId = accountId, document.SelectedNode);
            NotifyAccountBindingChanged();
            AccountMessage = accountId is null
                ? "已改为跟随当前账户，将随当前配置自动保存。"
                : "运行账户已更改，将随当前配置自动保存。";
        }
    }

    /// <summary>当前配置是否指定了账户；运行前会切换到该账户，因此清单首位显示“切换账户”准备项。</summary>
    public bool HasRunAccount => SelectedConfiguration?.Plan.AccountId is not null;

    /// <summary>配置标题旁是否显示“运行账户”：计划指定了账户，或账户目录有多个账户时；只有一个账户且跟随当前账户时不显示。</summary>
    public bool ShowRunAccountEntry => PuloniaRunAccountText.ShouldShow(SelectedConfiguration?.Plan.AccountId, GenshinAccounts.Count);

    /// <summary>配置标题旁显示的运行账户；指定的账户已删除或没有 UID 时明确说明。</summary>
    public string RunAccountText => PuloniaRunAccountText.Describe(SelectedConfiguration?.Plan.AccountId, GenshinAccounts);

    /// <summary>指定的运行账户不可用时的提示（账户已删除或没有 UID），显示在运行账户设置区；可用时为 null。</summary>
    public string? RunAccountProblem => PuloniaRunAccountText.GetProblem(SelectedConfiguration?.Plan.AccountId, GenshinAccounts);

    /// <summary>是否需要显示运行账户不可用的提示。</summary>
    public bool HasRunAccountProblem => RunAccountProblem is not null;

    /// <summary>运行账户、配置切换、撤销或账户目录刷新后同步所有账户相关显示。</summary>
    private void NotifyAccountBindingChanged()
    {
        OnPropertyChanged(nameof(SelectedGenshinAccountId));
        OnPropertyChanged(nameof(HasRunAccount));
        OnPropertyChanged(nameof(ShowRunAccountEntry));
        OnPropertyChanged(nameof(RunAccountText));
        OnPropertyChanged(nameof(RunAccountProblem));
        OnPropertyChanged(nameof(HasRunAccountProblem));
        // 运行账户设置决定“运行于”单选还是多选与默认实例；换配置或改设置后恢复默认选择
        RunTargets.SetPlan(SelectedConfiguration?.Id, SelectedConfiguration?.Plan.AccountId);
    }

    /// <summary>加载成功保存的账户目录（首次使用时建立“我的账户”），并重建运行账户选项。</summary>
    private async Task RefreshGenshinAccountsAsync()
    {
        var settings = await _store.LoadAccountSettingsAsync();
        GenshinAccounts.Clear(); foreach (var account in settings.Accounts) GenshinAccounts.Add(account);
        RunAccountOptions.Clear();
        foreach (var option in PuloniaRunAccountText.BuildOptions(settings.Accounts)) RunAccountOptions.Add(option);
        NotifyAccountBindingChanged();
    }

    /// <summary>选中固定的账户准备项，任务表仍保持原来的默认任务；运行账户入口也从这里进入。</summary>
    [RelayCommand] private void SelectAccountPreparation()
    { SelectedTask = null; IsAccountPreparationSelected = true; }

    /// <summary>跳转到账户页管理账户与登录资料；返回本页时重新读取账户目录。</summary>
    [RelayCommand] private void ManageGenshinAccounts() => _navigation?.Navigate(typeof(PuloniaAccountsPage));

    /// <summary>
    /// “更多 → 查看路线 CD”：跳到账户页的“路线 CD”，选中这份配置按统筹设计 5.6 选出的账户（指定账户或本实例当前账户），
    /// 并按这份配置筛选。本页不显示路线 CD 标记，因此没有“CD 按账户”选择器。
    /// </summary>
    [RelayCommand]
    private async Task ShowRouteCooldownsAsync()
    {
        if (SelectedConfiguration is not { } document || _navigation is null) return;
        // 账户目录可能尚未读取（本页不计算路线 CD），先确保读取后再选账户
        await Session.EnsureCooldownAccountsAsync();
        _accountsPage?.RequestShowRouteCooldowns(Session.GetDefaultCooldownAccount(document)?.Id, document.Id);
        _navigation.Navigate(typeof(PuloniaAccountsPage));
    }
}
