using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 账户页“概况”中的只读信息（统筹设计 9.3）：所选账户最近一次运行，以及指定了该账户的计划（点击跳转到对应页面）。
/// 只针对已保存的账户；页面进入、切换账户或保存后重新读取。
/// </summary>
public partial class PuloniaAccountsPageViewModel
{
    /// <summary>编辑会话：跳转到计划前记下要打开的计划。</summary>
    private readonly PuloniaPlanEditingSession _session;

    /// <summary>跨实例运行视图，读取所选账户最近一次运行；为空时不显示。</summary>
    private readonly PuloniaRunOverview? _runOverview;

    /// <summary>概况读取版本；切换账户后，较早读取的结果不再写入。</summary>
    private int _overviewVersion;

    /// <summary>最近一次运行的说明，例如“每日委托 · 已完成 · 2026-11-01 04:31 · 云原神 · a”。</summary>
    [ObservableProperty] private string _lastRunText = "";

    /// <summary>指定了所选账户的计划；没有时为空。</summary>
    public ObservableCollection<PuloniaAccountPlanUsage> PlanUsages { get; } = [];

    /// <summary>是否有计划指定了所选账户。</summary>
    public bool HasPlanUsages => PlanUsages.Count > 0;

    /// <summary>
    /// 重新读取所选账户的最近一次运行与指定了它的计划；新增尚未保存的账户没有这些信息。读取失败时只说明，不影响编辑。
    /// </summary>
    internal async Task RefreshOverviewAsync()
    {
        var version = ++_overviewVersion;
        var account = SelectedAccount;
        if (account?.Saved is null)
        {
            PlanUsages.Clear();
            OnPropertyChanged(nameof(HasPlanUsages));
            LastRunText = account is null ? "" : "保存后显示。";
            return;
        }
        // 1. 指定了该账户的计划
        string lastRun;
        PuloniaAccountPlanUsage[] usages;
        try
        {
            usages = (await _accounts.ListPlansSpecifyingAccountAsync(account.Id)).ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            usages = [];
        }
        // 2. 最近一次运行：本实例、其他实例与共享历史中运行账户为该账户、最晚提交的一次
        try
        {
            lastRun = _runOverview is null
                ? ""
                : (await _runOverview.ListAsync()).Runs.Where(run => run.AccountId == account.Id)
                    .OrderByDescending(run => run.SubmittedAt).FirstOrDefault() is { } run
                    ? DescribeRun(run)
                    : "还没有运行记录。";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            lastRun = "读取执行记录失败：" + ex.Message;
        }
        if (version != _overviewVersion)
            return;
        PlanUsages.Clear();
        foreach (var usage in usages)
            PlanUsages.Add(usage);
        OnPropertyChanged(nameof(HasPlanUsages));
        LastRunText = lastRun;
    }

    /// <summary>运行的一行说明：计划名 · 状态 · 时间 · 执行实例。</summary>
    /// <param name="run">运行</param>
    private static string DescribeRun(PuloniaTaskRunView run)
        => string.Join(" · ", run.PlanName, PuloniaTaskHistoryText.Status(run.Status),
            PuloniaTaskHistoryText.Time(run.FinishedAt ?? run.StartedAt ?? run.SubmittedAt), PuloniaInstanceKey.Describe(run.Instance));

    /// <summary>
    /// 跳转到指定了该账户的计划：普通计划打开任务计划页，一条龙配置打开新一条龙页；先记下请求再导航，目标页面加载后选中。
    /// </summary>
    /// <param name="usage">要打开的计划</param>
    [RelayCommand]
    private void OpenPlan(PuloniaAccountPlanUsage? usage)
    {
        if (usage is null || _navigation is null)
            return;
        _session.RequestOpenPlan(usage.PlanId);
        _navigation.Navigate(usage.Purpose == PuloniaTaskPlanPurpose.OneDragon ? typeof(PuloniaOneDragonPage) : typeof(PuloniaTaskPlanPage));
    }
}
