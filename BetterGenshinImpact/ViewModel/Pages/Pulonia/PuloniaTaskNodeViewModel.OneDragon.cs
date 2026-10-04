using System;
using System.Linq;
using System.Windows.Media;
using BetterGenshinImpact.Pulonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>一条龙平面清单的运行状态展示；不持久化、不参与执行判断。</summary>
public partial class PuloniaTaskNodeViewModel
{
    /// <summary>本行最近运行的状态说明，与当前编辑开关相互独立。</summary>
    [ObservableProperty] private string _oneDragonStatusText = "尚未执行";
    /// <summary>本行状态指示颜色。</summary>
    [ObservableProperty] private Brush _oneDragonStatusBrush = Brushes.Gray;

    /// <summary>一条龙清单直接展示本节点的有效队伍、策略、次数和参数来源。</summary>
    public string ConfigurationSummary => Document.GetOneDragonConfigurationSummary(this);

    /// <summary>父组、参数或预设发生变化时刷新清单摘要绑定。</summary>
    internal void NotifyConfigurationSummaryChanged() => OnPropertyChanged(nameof(ConfigurationSummary));

    /// <summary>按地址中的稳定节点 ID 聚合行内子任务结果，避免重复名称互相串状态。</summary>
    internal void UpdateOneDragonStatus(PuloniaTaskRunView? run)
    {
        var prefix = run is null ? null : run.PlanId + "/" + Document.RootNode.Id + "/" + Id;
        // 使用完整行地址，引用计划内部允许与其他计划使用相同的节点 ID。
        bool ContainsId(string? address) => prefix is not null && address is not null
            && (address == prefix || address.StartsWith(prefix + "/", StringComparison.Ordinal));
        var results = run?.NodeResults.Where(r => ContainsId(r.TaskAddress)).ToArray() ?? [];
        if (run?.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling && ContainsId(run.CurrentTaskAddress))
        { OneDragonStatusText = "执行中"; OneDragonStatusBrush = Brushes.DodgerBlue; }
        else if (results.Any(r => r.Status is PuloniaTaskNodeStatus.Failed or PuloniaTaskNodeStatus.TimedOut))
        { OneDragonStatusText = "执行失败"; OneDragonStatusBrush = Brushes.IndianRed; }
        else if (results.Any(r => r.Status == PuloniaTaskNodeStatus.NeedsAttention || r.OutcomeKind == PuloniaTaskOutcomeKind.ExecutedUnverified))
        { OneDragonStatusText = "待核验"; OneDragonStatusBrush = Brushes.DarkOrange; }
        else if (results.Length > 0 && results.All(r => r.Status == PuloniaTaskNodeStatus.Succeeded))
        { OneDragonStatusText = "已完成"; OneDragonStatusBrush = Brushes.ForestGreen; }
        else
        { OneDragonStatusText = results.Length == 0 ? "尚未执行" : "已跳过或停止"; OneDragonStatusBrush = Brushes.Gray; }
    }
}
