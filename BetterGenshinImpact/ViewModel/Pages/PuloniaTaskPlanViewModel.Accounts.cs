using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 任务计划页计划标题旁的“运行账户”：编辑当前计划的 <see cref="PuloniaTaskPlan.AccountId"/>，
/// 跟随当前账户（默认，不切换）或指定填写了 UID 的账户。修改走文档的自动保存与撤销；
/// 规则与文案和新一条龙页共用 <see cref="PuloniaRunAccountText"/>。
/// </summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>最近一次读取的账户目录，用于选项、显示规则与提示。</summary>
    private IReadOnlyList<PuloniaGenshinAccountProfile> _runAccounts = [];

    /// <summary>运行账户下拉框的选项：“跟随当前账户”加填写了 UID 的账户；键为账户 ID，跟随当前账户为空字符串。</summary>
    public ObservableCollection<KeyValuePair<string, string>> RunAccountOptions { get; } = [];

    /// <summary>当前计划的运行账户设置：空字符串表示跟随当前账户，否则为指定账户 ID。</summary>
    public string SelectedRunAccountId
    {
        get => SelectedDocument?.Plan.AccountId ?? PuloniaRunAccountText.FollowCurrentKey;
        set
        {
            // 下拉框重建选项时会先写回 null，不能据此把指定账户改成跟随当前账户。
            if (value is null || SelectedDocument is not { } document)
                return;
            var accountId = value.Length == 0 ? null : value;
            if (accountId == document.Plan.AccountId)
                return;
            // 只能指定填写了 UID 的账户，因为切换和核验都要按 UID 找到目标。
            if (accountId is not null && !PuloniaRunAccountText.CanSpecify(accountId, _runAccounts))
                return;
            document.ApplyMutation(() => document.Plan.AccountId = accountId, document.SelectedNode);
            NotifyRunAccountChanged();
            StatusMessage = accountId is null
                ? "已改为跟随当前账户，将自动保存。"
                : "运行账户已更改，运行前会切换到该账户并核验 UID；将自动保存。";
        }
    }

    /// <summary>计划标题旁是否显示“运行账户”：计划指定了账户，或账户目录有多个账户时；只有一个账户且跟随当前账户时不显示。</summary>
    public bool ShowRunAccountEntry => SelectedDocument is { } document
                                       && PuloniaRunAccountText.ShouldShow(document.Plan.AccountId, _runAccounts.Count);

    /// <summary>指定的运行账户不可用时的提示（账户已删除或没有 UID）；可用时为 null。</summary>
    public string? RunAccountProblem => PuloniaRunAccountText.GetProblem(SelectedDocument?.Plan.AccountId, _runAccounts);

    /// <summary>是否需要显示运行账户不可用的提示。</summary>
    public bool HasRunAccountProblem => RunAccountProblem is not null;

    /// <summary>运行账户、计划切换、撤销或账户目录刷新后同步运行账户相关显示。</summary>
    private void NotifyRunAccountChanged()
    {
        OnPropertyChanged(nameof(SelectedRunAccountId));
        OnPropertyChanged(nameof(ShowRunAccountEntry));
        OnPropertyChanged(nameof(RunAccountProblem));
        OnPropertyChanged(nameof(HasRunAccountProblem));
        // 运行账户决定“CD 按账户”的默认账户
        NotifyCooldownAccountChanged();
        // 运行账户也决定“运行于”单选还是多选与默认实例；换计划或改设置后恢复默认选择
        RunTargets.SetPlan(SelectedDocument?.Id, SelectedDocument?.Plan.AccountId);
    }

    /// <summary>读取账户目录并重建运行账户选项；读取失败时保留上一次的选项并在状态栏说明。</summary>
    private async Task RefreshRunAccountsAsync()
    {
        try
        {
            var settings = await Session.LoadAccountDirectoryAsync();
            _runAccounts = settings.Accounts;
            RunAccountOptions.Clear();
            foreach (var option in PuloniaRunAccountText.BuildOptions(settings.Accounts))
                RunAccountOptions.Add(option);
        }
        catch (Exception ex)
        {
            StatusMessage = "账户目录读取失败，运行账户无法显示：" + ex.Message;
        }
        NotifyRunAccountChanged();
    }
}
