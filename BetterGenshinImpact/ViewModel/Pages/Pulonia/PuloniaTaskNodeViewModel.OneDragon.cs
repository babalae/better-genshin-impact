using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>一条龙平面清单的运行状态展示；不持久化、不参与执行判断。</summary>
public partial class PuloniaTaskNodeViewModel
{
    /// <summary>本行最近运行的状态说明，与当前编辑开关相互独立。</summary>
    [ObservableProperty] private string _oneDragonStatusText = "尚未执行";
    /// <summary>本行状态指示的语义分类；颜色由视图按主题资源决定。</summary>
    [ObservableProperty] private PuloniaTaskSubtreeStatus _oneDragonStatusKind = PuloniaTaskSubtreeStatus.NotRun;

    /// <summary>一条龙清单直接展示本节点的有效队伍、策略、次数和参数来源。</summary>
    public string ConfigurationSummary => Document.GetOneDragonConfigurationSummary(this);

    /// <summary>一条龙设置中仅允许非内置任务编辑显示名称，内置独立任务保留只读名称。</summary>
    public bool CanEditOneDragonName => !PuloniaSoloTaskExecutor.IsSoloTaskType(TaskType);

    /// <summary>父组、参数或预设发生变化时刷新清单摘要绑定。</summary>
    internal void NotifyConfigurationSummaryChanged() => OnPropertyChanged(nameof(ConfigurationSummary));

    /// <summary>按地址中的稳定节点 ID 聚合行内子任务结果，避免重复名称互相串状态。</summary>
    internal void UpdateOneDragonStatus(PuloniaTaskRunView? run)
    {
        // 使用完整行地址，引用计划内部允许与其他计划使用相同的节点 ID；归类规则见 PuloniaTaskRunClassifier。
        OneDragonStatusKind = PuloniaTaskRunClassifier.ClassifySubtree(run, Document.RootNode.Id, Id);
        OneDragonStatusText = OneDragonStatusKind switch
        {
            PuloniaTaskSubtreeStatus.Running => "执行中",
            PuloniaTaskSubtreeStatus.Failed => "执行失败",
            PuloniaTaskSubtreeStatus.NeedsVerification => "待核验",
            PuloniaTaskSubtreeStatus.Completed => "已完成",
            PuloniaTaskSubtreeStatus.SkippedOrStopped => "已跳过或停止",
            _ => "尚未执行"
        };
    }
}
