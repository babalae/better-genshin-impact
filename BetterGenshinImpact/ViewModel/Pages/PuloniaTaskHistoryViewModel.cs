using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 全局执行记录和计划内历史共用的视图模型，维护记录列表、详情选择与安全续跑。
/// </summary>
public partial class PuloniaTaskHistoryViewModel : ViewModel
{
    /// <summary>
    /// 历史、队列、当前状态与续跑的统一服务。
    /// </summary>
    private readonly IPuloniaTaskService _taskService;

    /// <summary>
    /// 当前加载的全部运行，不随计划过滤丢弃。
    /// </summary>
    private readonly List<PuloniaTaskRunItemViewModel> _allRuns = [];

    /// <summary>
    /// 执行器注册的任务类型显示名称。
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> _typeNames;

    /// <summary>
    /// 串行化运行列表刷新，避免多个节点事件同时重建集合。
    /// </summary>
    private readonly SemaphoreSlim _runRefreshGate = new(1, 1);

    /// <summary>
    /// 合并后台事件刷新，避免短时间多次上报堆积 UI 工作。
    /// </summary>
    private bool _eventRefreshPending;

    /// <summary>
    /// 当前读取期间又收到新事件时，再取一次最新状态，不能漏掉最终归档通知。
    /// </summary>
    private bool _eventRefreshAgain;

    /// <summary>
    /// 本地保存目录，供用户定位历史文件；不会自动上传。
    /// </summary>
    public string StorageDirectory { get; }

    /// <summary>
    /// 当前选中的进程内运行状态或本地历史。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskRunItemViewModel? _selectedRun;

    /// <summary>
    /// 运行详情中选中的节点尝试，用于明确指定续跑起点。
    /// </summary>
    [ObservableProperty]
    private PuloniaTaskNodeResultViewModel? _selectedRunNodeResult;

    /// <summary>
    /// 计划页固定过滤的当前计划 ID，全局页为空；为空时展示全部计划的记录。
    /// </summary>
    [ObservableProperty]
    private string? _planFilterId;

    /// <summary>
    /// 最近一次刷新或操作的结果，包括读取失败。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "记录自动保存到本地，重新启动后仍可查看。";

    /// <summary>
    /// 正在刷新本地历史。
    /// </summary>
    [ObservableProperty]
    private bool _isRefreshing;

    /// <summary>
    /// 当前队列、活动运行与已经落盘的不可变历史。
    /// </summary>
    public ObservableCollection<PuloniaTaskRunItemViewModel> Runs { get; } = [];

    /// <summary>
    /// 当前仍约束执行的 CD 与周期额度事实，不是选中历史当时的全局账本。
    /// </summary>
    public ObservableCollection<PuloniaTaskLedgerItemViewModel> LedgerEntries { get; } = [];

    /// <summary>
    /// 列表为空时的解释，不把计划过滤无结果误显示为历史丢失。
    /// </summary>
    public string EmptyText => _allRuns.Count == 0 ? "暂无执行记录\n运行任务计划后，记录会自动保存在本地。" : "当前计划暂无执行记录。";

    /// <summary>
    /// 当前显示的记录计数。
    /// </summary>
    public string CountText => $"显示 {Runs.Count} / {_allRuns.Count} 条记录（新到旧）";

    /// <summary>
    /// 记录卡片是否展示计划名标题；计划页固定只看当前计划，标题与页面计划名重复，仅全局页跨计划展示时显示。
    /// </summary>
    public bool ShowPlanTitle => PlanFilterId is null;

    /// <summary>
    /// 是否已选择运行。
    /// </summary>
    public bool HasSelectedRun => SelectedRun is not null;

    /// <summary>
    /// 是否已选择节点尝试。
    /// </summary>
    public bool HasSelectedNode => SelectedRunNodeResult is not null;

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
    /// 注入统一运行服务；只订阅一次，页面与嵌入组件共享此单例。
    /// </summary>
    public PuloniaTaskHistoryViewModel(IPuloniaTaskService taskService, PuloniaTaskStore store)
    {
        _taskService = taskService;
        StorageDirectory = store.RootDirectory;
        _typeNames = taskService.Definitions.ToDictionary(item => item.TaskType, item => item.DisplayName);
        _taskService.RunChanged += OnRunChanged;
    }

    /// <summary>
    /// 全局记录页面不依赖现存计划文档，即使计划已删除仍可查看其历史。
    /// </summary>
    public override async Task OnNavigatedToAsync()
    {
        PlanFilterId = null;
        await RefreshAsync();
    }

    /// <summary>
    /// Loaded 行为清除计划范围，兼容由页面自身作为 DataContext 的导航方式。
    /// </summary>
    [RelayCommand]
    private Task OpenPageAsync() => OnNavigatedToAsync();

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
            await RefreshAsync(selected.RequestId);
            StatusMessage = "取消请求已处理；请以运行详情中的最终状态及核验提示为准。";
        }
        catch (Exception ex)
        {
            await ShowOperationErrorAsync("取消运行失败：", ex);
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
            await RevealRunAsync(requestId);
            StatusMessage = $"已沿用历史运行 {selected.Run.RunId:D} 的固定快照继续未完成节点。";
        }
        catch (Exception ex)
        {
            await ShowOperationErrorAsync("继续历史运行失败：", ex);
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
        // 显式起点会重放其后的节点，已确认成功也可能重复，普通历史同样需要确认。
        var result = await ThemedMessageBox.ShowAsync(
            $"将从“{node.TaskName}”开始建立新运行，起点及之后的节点可能重复执行。"
            + (allowUncertainReplay ? "\n此历史包含未核验操作，请先核对游戏状态；确认后会释放未决占用。" : "")
            + "\n是否确认？", "确认从节点开始", MessageBoxButton.YesNo,
            ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;
        try
        {
            var requestId = await _taskService.ResumeAsync(selected.Run.RunId, node.TaskAddress, allowUncertainReplay);
            await RevealRunAsync(requestId);
            StatusMessage = $"已沿用原快照，从节点 {node.TaskAddress} 开始关联运行。";
        }
        catch (Exception ex)
        {
            await ShowOperationErrorAsync("从节点继续失败：", ex);
        }
    }

    /// <summary>
    /// 从服务读取最新运行状态，并尽量保持原选中请求。
    /// </summary>
    [RelayCommand]
    private Task RefreshRunsAsync() => RefreshAsync();

    /// <summary>
    /// 用户主动提交或续跑时显示新请求，立即定位到该运行的最新状态。
    /// </summary>
    public async Task RevealRunAsync(Guid requestId)
    {
        await RefreshAsync(requestId);
    }

    /// <summary>
    /// 刷新运行列表并优先选中指定请求，后台事件不抢占用户正在查看的记录。
    /// </summary>
    public async Task RefreshAsync(Guid? preferredRequestId = null)
    {
        await _runRefreshGate.WaitAsync();
        IsRefreshing = true;
        try
        {
            var runs = await _taskService.ListRunsAsync();
            var ledger = await _taskService.ListLedgerAsync();
            // 已归档历史不会改变，复用条目避免每个实时事件重建旧快照和滚动内容。
            var archived = _allRuns.Where(item => item.IsHistorical).ToDictionary(item => item.Run.RunId);
            _allRuns.Clear();
            foreach (var run in runs.OrderByDescending(item => item.SubmittedAt))
                _allRuns.Add(run.IsHistorical && archived.TryGetValue(run.RunId, out var cached)
                    ? cached : new PuloniaTaskRunItemViewModel(run, _typeNames));
            LedgerEntries.Clear();
            foreach (var entry in ledger.OrderByDescending(item => item.OccurredAt))
                LedgerEntries.Add(new PuloniaTaskLedgerItemViewModel(entry));
            ApplyPlanFilter(preferredRequestId);
            StatusMessage = _taskService.RecoveryNotice ?? "记录已更新；所有时间按本地时区显示。";
        }
        catch (Exception ex)
        {
            StatusMessage = "刷新运行状态失败：" + ex.Message;
        }
        finally
        {
            IsRefreshing = false;
            _runRefreshGate.Release();
        }
    }

    /// <summary>
    /// 服务状态变化时把刷新安排到 WPF UI 线程。
    /// </summary>
    private void OnRunChanged(object? sender, PuloniaTaskRunChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
            return;
        _ = dispatcher.InvokeAsync(async () =>
        {
            _eventRefreshAgain = true;
            if (_eventRefreshPending)
                return;
            _eventRefreshPending = true;
            try
            {
                while (_eventRefreshAgain)
                {
                    // 给一批节点/确认事件一个合并窗口，不阻塞 UI 线程。
                    await Task.Delay(100);
                    _eventRefreshAgain = false;
                    await RefreshAsync();
                }
            }
            finally
            {
                _eventRefreshPending = false;
            }
        });
    }

    /// <summary>
    /// 在 UI 线程按计划范围同步列表，并按请求、节点地址、尝试次数恢复选择。
    /// </summary>
    private void ApplyPlanFilter(Guid? preferredRequestId = null)
    {
        var selectedRequestId = preferredRequestId ?? SelectedRun?.RequestId;
        var selectedAddress = SelectedRunNodeResult?.TaskAddress;
        var selectedAttempt = SelectedRunNodeResult?.Attempt;
        var filtered = _allRuns.Where(item => PlanFilterId is null || item.Run.PlanId == PlanFilterId).ToArray();
        // 同步差异而非清空列表，保持选中历史条目和虚拟化列表的滚动位置。
        for (var index = 0; index < filtered.Length; index++)
        {
            if (index < Runs.Count && ReferenceEquals(Runs[index], filtered[index]))
                continue;
            var oldIndex = Runs.IndexOf(filtered[index]);
            if (oldIndex >= 0)
                Runs.Move(oldIndex, index);
            else
                Runs.Insert(index, filtered[index]);
        }
        while (Runs.Count > filtered.Length)
            Runs.RemoveAt(Runs.Count - 1);
        // 未选中也是有效状态；后台刷新和计划切换不得强制选回第一条历史。
        // 只有显式提交/续跑指定请求，或保留用户正在查看的请求时才恢复选择。
        SelectedRun = selectedRequestId is { } requestId
            ? Runs.FirstOrDefault(item => item.RequestId == requestId)
            : null;
        if (SelectedRun is { } current && current.RequestId == selectedRequestId)
            SelectedRunNodeResult = current.NodeDetails.FirstOrDefault(item =>
                item.TaskAddress == selectedAddress && item.Attempt == selectedAttempt) ?? SelectedRunNodeResult;
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>
    /// 使用主题对话框报告操作失败，不影响后台任务执行。
    /// </summary>
    private async Task ShowOperationErrorAsync(string prefix, Exception ex)
    {
        StatusMessage = prefix + ex.Message;
        await ThemedMessageBox.ErrorAsync(StatusMessage, "执行记录操作失败");
    }

    /// <summary>
    /// 选中运行变化后刷新取消命令。
    /// </summary>
    partial void OnSelectedRunChanged(PuloniaTaskRunItemViewModel? value)
    {
        SelectedRunNodeResult = value?.NodeDetails.LastOrDefault(item =>
                                    item.Result.Status is PuloniaTaskNodeStatus.Failed
                                        or PuloniaTaskNodeStatus.TimedOut
                                        or PuloniaTaskNodeStatus.Cancelled
                                        or PuloniaTaskNodeStatus.NeedsAttention)
                                ?? value?.NodeDetails.LastOrDefault();
        OnPropertyChanged(nameof(HasSelectedRun));
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
    partial void OnSelectedRunNodeResultChanged(PuloniaTaskNodeResultViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedNode));
        OnPropertyChanged(nameof(CanResumeFromNode));
        ResumeFromNodeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 计划范围变化立即同步列表，并更新卡片标题的显隐。
    /// </summary>
    partial void OnPlanFilterIdChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowPlanTitle));
        ApplyPlanFilter();
    }
}
