using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// “任务计划”页面的步骤 3 运行提交、状态查看和取消命令。
/// </summary>
public partial class PuloniaTaskPlanViewModel
{
    /// <summary>
    /// 串行化运行列表刷新，避免多个节点事件同时重建集合。
    /// </summary>
    private readonly SemaphoreSlim _runRefreshGate = new(1, 1);

    /// <summary>
    /// 页面默认提交的计划总时限，单位秒。
    /// </summary>
    [ObservableProperty]
    private double _runTimeoutSeconds = 600;

    /// <summary>
    /// 当前选中的进程内运行状态。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskRunItemViewModel? _selectedRun;

    /// <summary>
    /// 运行详情中选中的节点尝试，用于明确指定续跑起点。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskNodeResult? _selectedRunNodeResult;

    /// <summary>
    /// 当前队列、活动运行与已经落盘的不可变历史。
    /// </summary>
    public ObservableCollection<PuloniaTaskRunItemViewModel> Runs { get; } = [];

    /// <summary>
    /// 当前仍约束执行的 CD 与周期额度事实。
    /// </summary>
    public ObservableCollection<PuloniaTaskLedgerItemViewModel> LedgerEntries { get; } = [];

    /// <summary>
    /// 当前计划是否可以提交运行。
    /// </summary>
    public bool CanRunPlan => !IsBusy && SelectedDocument is not null;

    /// <summary>
    /// 当前选中运行是否可以显式取消。
    /// </summary>
    public bool CanCancelRun => SelectedRun?.CanCancel == true;

    /// <summary>
    /// 当前选中历史是否可以继续剩余节点。
    /// </summary>
    public bool CanResumeRun => SelectedRun?.CanResume == true;

    /// <summary>
    /// 是否可以从选中的历史节点地址建立关联运行。
    /// </summary>
    public bool CanResumeFromNode => SelectedRun?.IsHistorical == true && SelectedRunNodeResult is not null;

    /// <summary>
    /// 保存当前计划后提交运行；提交时固定快照，随后编辑不会影响本次运行。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunPlan))]
    private async Task RunPlanAsync()
    {
        var document = SelectedDocument;
        if (document is null)
            return;
        if (document.IsDirty && !await SaveDocumentAsync(document))
            return;

        try
        {
            var requestId = await _taskService.EnqueueAsync(new PuloniaTaskRequest
            {
                PlanId = document.Id,
                TimeoutSeconds = RunTimeoutSeconds,
                Source = "ui"
            });
            await RefreshRunsAsync(requestId);
            StatusMessage = $"已提交“{document.Name}”，请求 {requestId:D} 已固定快照并进入串行队列。";
        }
        catch (Exception ex)
        {
            StatusMessage = "提交运行失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法运行任务计划");
        }
    }

    /// <summary>
    /// 显式取消选中运行，并等待当前执行器真正退出。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private async Task CancelRunAsync()
    {
        var selected = SelectedRun;
        if (selected is null)
            return;
        try
        {
            StatusMessage = "正在取消运行，并等待当前执行器释放资源……";
            await _taskService.CancelAsync(selected.RequestId);
            await RefreshRunsAsync(selected.RequestId);
            StatusMessage = "运行已取消，当前执行器已确认退出。";
        }
        catch (Exception ex)
        {
            StatusMessage = "取消运行失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法取消任务计划");
        }
    }

    /// <summary>
    /// 沿用选中历史的原快照，跳过已经确认成功的节点并继续剩余内容。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResumeRun))]
    private async Task ResumeRunAsync()
    {
        var selected = SelectedRun;
        if (selected is null)
            return;
        try
        {
            var requestId = await _taskService.ResumeAsync(selected.Run.RunId);
            await RefreshRunsAsync(requestId);
            StatusMessage = $"已沿用历史运行 {selected.Run.RunId:D} 的固定快照继续未完成节点。";
        }
        catch (Exception ex)
        {
            StatusMessage = "继续历史运行失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法继续任务计划");
        }
    }

    /// <summary>
    /// 从用户选中的快照节点开始新运行；待处理运行需要用户明确承担重放选择。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResumeFromNode))]
    private async Task ResumeFromNodeAsync()
    {
        var selected = SelectedRun;
        var node = SelectedRunNodeResult;
        if (selected is null || node is null)
            return;
        var allowUncertainReplay = selected.Run.HasUncertainOperation
                                   || selected.Run.Status == PuloniaTaskRunStatus.NeedsAttention
                                   || selected.Run.NodeResults
                                       .GroupBy(item => item.TaskAddress, StringComparer.Ordinal)
                                       .Select(group => group.Last())
                                       .Any(item => item.OutcomeKind is PuloniaTaskOutcomeKind.ExecutedUnverified
                                           or PuloniaTaskOutcomeKind.PartiallySucceeded
                                           or PuloniaTaskOutcomeKind.NeedsAttention);
        if (allowUncertainReplay)
        {
            var result = await ThemedMessageBox.ShowAsync(
                "此历史包含未核验操作。请先核对游戏当前状态；继续将释放该未决占用并从所选节点重新执行，是否确认？",
                "确认待处理续跑", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
                return;
        }

        try
        {
            var requestId = await _taskService.ResumeAsync(selected.Run.RunId, node.TaskAddress,
                allowUncertainReplay);
            await RefreshRunsAsync(requestId);
            StatusMessage = $"已沿用原快照，从节点 {node.TaskAddress} 开始关联运行。";
        }
        catch (Exception ex)
        {
            StatusMessage = "从节点继续失败：" + ex.Message;
            await ThemedMessageBox.ErrorAsync(StatusMessage, "无法从节点继续");
        }
    }

    /// <summary>
    /// 从服务读取最新运行状态，并尽量保持原选中请求。
    /// </summary>
    [RelayCommand]
    private async Task RefreshRunsAsync() => await RefreshRunsAsync(SelectedRun?.RequestId);

    /// <summary>
    /// 刷新运行列表并优先选中指定请求。
    /// </summary>
    private async Task RefreshRunsAsync(Guid? preferredRequestId)
    {
        await _runRefreshGate.WaitAsync();
        try
        {
            var selectedRequestId = preferredRequestId ?? SelectedRun?.RequestId;
            var runs = await _taskService.ListRunsAsync();
            var ledger = await _taskService.ListLedgerAsync();
            Runs.Clear();
            foreach (var run in runs)
                Runs.Add(new PuloniaTaskRunItemViewModel(run));
            LedgerEntries.Clear();
            foreach (var entry in ledger.OrderByDescending(item => item.OccurredAt))
                LedgerEntries.Add(new PuloniaTaskLedgerItemViewModel(entry));
            SelectedRun = Runs.FirstOrDefault(item => item.RequestId == selectedRequestId) ?? Runs.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = "刷新运行状态失败：" + ex.Message;
        }
        finally
        {
            _runRefreshGate.Release();
        }
    }

    /// <summary>
    /// 服务状态变化时把刷新安排到 WPF UI 线程。
    /// </summary>
    private void OnRunChanged(object? sender, PuloniaTaskRunChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;
        if (dispatcher.CheckAccess())
            _ = RefreshRunsAsync(e.RequestId);
        else
            _ = dispatcher.InvokeAsync(() => _ = RefreshRunsAsync(e.RequestId));
    }

    /// <summary>
    /// 选中运行变化后刷新取消命令。
    /// </summary>
    partial void OnSelectedRunChanged(PuloniaTaskRunItemViewModel? value)
    {
        SelectedRunNodeResult = value?.NodeResults.LastOrDefault(item =>
                                    item.Status is PuloniaTaskNodeStatus.Failed
                                        or PuloniaTaskNodeStatus.TimedOut
                                        or PuloniaTaskNodeStatus.Cancelled
                                        or PuloniaTaskNodeStatus.NeedsAttention)
                                ?? value?.NodeResults.LastOrDefault();
        OnPropertyChanged(nameof(CanCancelRun));
        OnPropertyChanged(nameof(CanResumeRun));
        OnPropertyChanged(nameof(CanResumeFromNode));
        CancelRunCommand.NotifyCanExecuteChanged();
        ResumeRunCommand.NotifyCanExecuteChanged();
        ResumeFromNodeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 节点结果选择变化后刷新从节点续跑命令。
    /// </summary>
    partial void OnSelectedRunNodeResultChanged(PuloniaTaskNodeResult? value)
    {
        OnPropertyChanged(nameof(CanResumeFromNode));
        ResumeFromNodeCommand.NotifyCanExecuteChanged();
    }
}
