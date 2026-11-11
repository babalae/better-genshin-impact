using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages;

/// <summary>
/// 全局执行记录和计划内历史共用的视图模型，维护记录列表、详情选择与安全续跑。
/// 列表显示所有实例的运行（统筹设计 6.5）：本实例的运行实时更新，其他实例的运行与共享历史在视图可见时每 5 秒比较一次修改标记，有变化才重新读取。
/// </summary>
public partial class PuloniaTaskHistoryViewModel : ViewModel, IDisposable
{
    /// <summary>
    /// 视图可见时比较其他实例运行状态与共享历史修改标记的间隔。
    /// </summary>
    private static readonly TimeSpan ChangeCheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 历史、队列、当前状态与续跑的统一服务。
    /// </summary>
    private readonly IPuloniaTaskService _taskService;

    /// <summary>
    /// 跨实例运行视图：合并本实例内存中的运行、其他实例运行状态中的未归档运行与共享历史。
    /// </summary>
    private readonly PuloniaRunOverview _overview;

    /// <summary>
    /// 主实例下发到子实例的运行跟踪器，用于停止下发的运行；未注册时为空。
    /// </summary>
    private readonly PuloniaRunDispatcher? _dispatcher;

    /// <summary>
    /// 正在显示本视图模型的视图；全局页、计划页页签和一条龙记录窗口可能先后加载与卸载，全部卸载后才停止定时比较。
    /// </summary>
    private readonly HashSet<object> _watchers = [];

    /// <summary>
    /// 定时比较修改标记的取消源；没有可见视图时为空。
    /// </summary>
    private CancellationTokenSource? _watchCancellation;

    /// <summary>
    /// 最近一次刷新所依据的修改标记；后台定时比较读取，UI 线程刷新时写入。
    /// </summary>
    private string? _lastChangeStamp;

    /// <summary>
    /// 最近一次刷新显示的读取提示（恢复提示、读不出的其他实例运行状态）；提示消失后据此清除说明。
    /// </summary>
    private string? _lastNotice;

    /// <summary>
    /// 当前加载的全部运行，不随计划过滤丢弃。
    /// </summary>
    private readonly List<PuloniaTaskRunItemViewModel> _allRuns = [];

    /// <summary>
    /// 执行器注册的任务类型显示名称。
    /// </summary>
    private readonly IReadOnlyDictionary<string, string> _typeNames;

    /// <summary>
    /// 读取账户目录以显示运行账户名称。
    /// </summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>
    /// 串行化运行列表刷新，避免多个节点事件同时重建集合。
    /// </summary>
    private readonly SemaphoreSlim _runRefreshGate = new(1, 1);

    /// <summary>
    /// 合并后台事件刷新，避免短时间多次上报堆积 UI 工作；读取期间又收到新事件时再取一次最新状态，不漏掉最终归档通知。
    /// </summary>
    private readonly PuloniaCoalescedRefresh _eventRefresh;

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
    /// 最近一次操作的结果、读取失败或恢复提示；没有需要告知的内容时为空，界面隐藏。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// 上一次刷新是否失败；刷新恢复后清除失败提示，但不覆盖用户操作的结果说明。
    /// </summary>
    private bool _lastRefreshFailed;

    /// <summary>
    /// 当前队列、活动运行与已经落盘的不可变历史。
    /// </summary>
    public ObservableCollection<PuloniaTaskRunItemViewModel> Runs { get; } = [];

    /// <summary>
    /// 列表为空时的解释，不把计划过滤无结果误显示为历史丢失。
    /// </summary>
    public string EmptyText => PlanFilterId is null
        ? "暂无执行记录\n执行任务计划或一条龙后，结果会显示在这里。"
        : "这个计划还没有执行记录\n开始执行后，结果会显示在这里。";

    /// <summary>
    /// 当前显示的记录数，按时间从新到旧排列。
    /// </summary>
    public string CountText => $"共 {Runs.Count} 条";

    /// <summary>
    /// 记录卡片是否展示计划名标题；计划页固定只看当前计划，标题与页面计划名重复，仅全局页跨计划展示时显示。
    /// </summary>
    public bool ShowPlanTitle => PlanFilterId is null;

    /// <summary>
    /// 记录卡片是否显示执行实例：当前列表中有子实例的运行记录时每条都显示，只有主实例的运行时不显示。
    /// </summary>
    [ObservableProperty]
    private bool _showInstanceBadges;

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
    public bool CanResumeFromNode => SelectedRun?.IsHistorical == true && SelectedRunNodeResult is not null
        && SelectedRunNodeResult.Result.TaskType != "genshin.account.prepare";

    /// <summary>
    /// 注入统一运行服务。全局记录页与任务计划页共享注入的单例；一条龙记录窗口另建实例，
    /// 固定自己的计划范围，关闭时调用 <see cref="Dispose"/> 解除订阅，不改写单例的过滤状态。
    /// </summary>
    /// <param name="taskService">本实例的任务服务</param>
    /// <param name="store">共享的 Pulonia 存储</param>
    /// <param name="runOverview">跨实例运行视图；为空时按主实例建立（测试用）</param>
    /// <param name="runDispatcher">主实例下发运行的跟踪器；为空时不能停止下发的运行</param>
    public PuloniaTaskHistoryViewModel(IPuloniaTaskService taskService, PuloniaTaskStore store,
        PuloniaRunOverview? runOverview = null, PuloniaRunDispatcher? runDispatcher = null)
    {
        _taskService = taskService;
        _store = store;
        _overview = runOverview ?? new PuloniaRunOverview(taskService, store, PuloniaInstanceKey.Primary);
        _dispatcher = runDispatcher;
        StorageDirectory = store.RootDirectory;
        _typeNames = taskService.Definitions.ToDictionary(item => item.TaskType, item => item.DisplayName);
        // 给一批节点结果与本次完成的路线一个合并窗口，不阻塞 UI 线程。
        _eventRefresh = new PuloniaCoalescedRefresh(() => RefreshAsync(), TimeSpan.FromMilliseconds(100));
        _taskService.RunChanged += OnRunChanged;
        // 下发运行的状态或连接变化（可能来自管道后台线程）同样合并刷新
        if (_dispatcher is not null)
            _dispatcher.Changed += OnDispatchedRunsChanged;
    }

    /// <summary>
    /// 解除运行事件订阅并停止定时比较；只用于窗口专属的实例，共享单例随程序存在。
    /// </summary>
    public void Dispose()
    {
        _taskService.RunChanged -= OnRunChanged;
        if (_dispatcher is not null)
            _dispatcher.Changed -= OnDispatchedRunsChanged;
        _watchers.Clear();
        StopChangeCheck();
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
    /// 执行记录视图加载（页面、页签或窗口可见）时登记，开始每 5 秒比较其他实例运行状态与共享历史的修改标记。
    /// </summary>
    /// <param name="view">加载的视图，用于与卸载配对</param>
    [RelayCommand]
    private void StartWatching(object? view)
    {
        if (view is null || !_watchers.Add(view) || _watchCancellation is not null
            || Application.Current?.Dispatcher is not { } dispatcher)
            return;
        _watchCancellation = new CancellationTokenSource();
        _ = WatchChangesAsync(dispatcher, _watchCancellation.Token);
    }

    /// <summary>
    /// 执行记录视图卸载时注销；全部视图都卸载后停止定时比较，运行事件仍会刷新列表。
    /// </summary>
    /// <param name="view">卸载的视图</param>
    [RelayCommand]
    private void StopWatching(object? view)
    {
        if (view is null || !_watchers.Remove(view) || _watchers.Count > 0)
            return;
        StopChangeCheck();
    }

    /// <summary>
    /// 停止定时比较。
    /// </summary>
    private void StopChangeCheck()
    {
        if (_watchCancellation is not { } cancellation)
            return;
        _watchCancellation = null;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    /// <summary>
    /// 视图可见期间先比较一次，之后每 5 秒比较一次修改标记（只读取文件与目录元数据），与上次刷新不同才在 UI 线程刷新。
    /// </summary>
    /// <param name="dispatcher">UI 调度器</param>
    /// <param name="ct">视图全部卸载时取消</param>
    private async Task WatchChangesAsync(Dispatcher dispatcher, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(ChangeCheckInterval);
        try
        {
            do
            {
                // 1. 在后台线程比较标记；目录暂时不可访问时等下一次
                string stamp;
                try
                {
                    stamp = await Task.Run(() => _overview.GetChangeStamp(), ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if (stamp == Volatile.Read(ref _lastChangeStamp))
                    continue;

                // 2. 有变化才刷新；刷新记下新的标记
                if (dispatcher.HasShutdownStarted)
                    return;
                await dispatcher.InvokeAsync(() => RefreshAsync(), DispatcherPriority.Background, ct).Task.Unwrap()
                    .ConfigureAwait(false);
            } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || dispatcher.HasShutdownStarted)
        {
            // 视图卸载或程序退出，正常结束。
        }
        catch (Exception ex)
        {
            // 比较失败不得成为未处理异常；下次视图加载会重新开始。
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>
    /// 显式取消选中运行：本实例的运行等待当前执行器真正退出；主实例下发的运行请求子实例停止，最终状态随后上报。
    /// 其他实例自己的运行和连接已断开的下发运行不能从这里停止（按钮不可用）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private async Task CancelRunAsync()
    {
        var selected = SelectedRun;
        if (selected is null)
            return;
        try
        {
            if (selected.StopAccess == PuloniaRunStopAccess.Dispatched && _dispatcher is { } dispatcher)
            {
                StatusMessage = $"正在请求“{selected.InstanceText}”停止……";
                await dispatcher.CancelAsync(selected.RunId);
                await RefreshAsync(selected.RunId);
                StatusMessage = "已请求停止，执行实例结束运行后状态会自动更新。";
                return;
            }
            StatusMessage = "正在停止，等待当前任务退出……";
            await _taskService.CancelAsync(selected.RunId);
            await RefreshAsync(selected.RunId);
            StatusMessage = "已停止。";
        }
        catch (Exception ex)
        {
            await ShowOperationErrorAsync("停止失败：", ex);
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
            var runId = await _taskService.ResumeAsync(selected.Run.RunId);
            await RevealRunAsync(runId);
            StatusMessage = "已开始续跑：沿用这次运行当时的设置，继续未完成的任务。";
        }
        catch (Exception ex)
        {
            await ShowOperationErrorAsync("续跑失败：", ex);
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
        // 待处理运行或任一节点最后一次结果不确定时，需要用户明确承担重放选择。
        var allowUncertainReplay = PuloniaTaskRunClassifier.RequiresReplayConfirmation(selected.Run.Status,
            selected.Run.NodeResults);
        // 显式起点会重放其后的节点，已确认成功也可能重复，普通历史同样需要确认。
        var result = await ThemedMessageBox.ShowAsync(
            $"将沿用这次运行当时的设置，从“{node.TaskName}”开始重新执行，它和之后的任务都会再执行一次。"
            + (allowUncertainReplay ? "\n这次运行有结果无法确认的任务，请先在游戏中核对。" : "")
            + "\n是否继续？", "从选中任务开始", MessageBoxButton.YesNo,
            ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
            return;
        try
        {
            var runId = await _taskService.ResumeAsync(selected.Run.RunId, node.TaskAddress, allowUncertainReplay);
            await RevealRunAsync(runId);
            StatusMessage = $"已开始续跑：沿用这次运行当时的设置，从“{node.TaskName}”开始。";
        }
        catch (Exception ex)
        {
            await ShowOperationErrorAsync("从选中任务开始失败：", ex);
        }
    }

    /// <summary>
    /// 从服务读取最新运行状态，并尽量保持原选中请求。
    /// </summary>
    [RelayCommand]
    private Task RefreshRunsAsync() => RefreshAsync();

    /// <summary>
    /// 用户主动提交或续跑时显示新运行，立即定位到该运行的最新状态。
    /// </summary>
    public async Task RevealRunAsync(string runId)
    {
        await RefreshAsync(runId);
    }

    /// <summary>
    /// 读取账户 ID → 名称；账户目录读不出时返回空表，执行记录改为显示账户 ID，不影响列表刷新。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> LoadAccountNamesAsync()
    {
        try
        {
            var accounts = await _store.LoadAccountSettingsAsync();
            return accounts.Accounts.ToDictionary(account => account.Id, account => account.Name, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 刷新运行列表并优先选中指定运行，后台事件不抢占用户正在查看的记录。
    /// 列表合并所有实例的运行：本实例内存中的运行、其他实例运行状态中的未归档运行与共享历史，同一运行以历史为准。
    /// </summary>
    public async Task RefreshAsync(string? preferredRunId = null)
    {
        await _runRefreshGate.WaitAsync();
        try
        {
            var overview = await _overview.ListAsync();
            var accountNames = await LoadAccountNamesAsync();
            // 主实例下发的运行：未结束时经下发客户端停止，连接断开时不能远程停止
            var dispatched = _dispatcher?.ListRuns().ToDictionary(run => run.RunId, StringComparer.Ordinal)
                             ?? new Dictionary<string, PuloniaDispatchedRun>(StringComparer.Ordinal);
            // 已归档历史不会改变，复用条目避免每个实时事件重建旧快照和滚动内容。
            var archived = _allRuns.Where(item => item.IsHistorical).ToDictionary(item => item.Run.RunId);
            _allRuns.Clear();
            foreach (var run in overview.Runs)
                _allRuns.Add(run.IsHistorical && archived.TryGetValue(run.RunId, out var cached)
                    ? cached : new PuloniaTaskRunItemViewModel(run, _typeNames, accountNames, GetStopAccess(run, dispatched)));
            Volatile.Write(ref _lastChangeStamp, overview.ChangeStamp);
            ApplyPlanFilter(preferredRunId);
            // 成功刷新只在有恢复提示、读不出的其他实例运行状态，或需要清除上次失败与已消失的提示时改写说明，保留用户操作的结果
            var notice = string.Join("\n", new[] { _taskService.RecoveryNotice, DescribeStateFailures(overview.StateFailures) }
                .OfType<string>());
            if (notice.Length > 0)
                StatusMessage = notice;
            else if (_lastRefreshFailed || (_lastNotice is not null && StatusMessage == _lastNotice))
                StatusMessage = string.Empty;
            _lastNotice = notice.Length > 0 ? notice : null;
            _lastRefreshFailed = false;
        }
        catch (Exception ex)
        {
            StatusMessage = "刷新执行记录失败：" + ex.Message;
            _lastRefreshFailed = true;
        }
        finally
        {
            _runRefreshGate.Release();
        }
    }

    /// <summary>
    /// 服务状态变化时把刷新安排到 WPF UI 线程。
    /// </summary>
    private void OnRunChanged(object? sender, PuloniaTaskRunChangedEventArgs e) => _eventRefresh.Request();

    /// <summary>
    /// 下发运行的状态或连接变化时同样合并刷新；事件可能来自管道后台线程。
    /// </summary>
    private void OnDispatchedRunsChanged(object? sender, EventArgs e) => _eventRefresh.Request();

    /// <summary>
    /// 未结束的运行能否从本实例停止：本实例的运行照常停止；主实例下发的运行经下发客户端停止，连接断开时不可用；
    /// 其他实例自己的运行只能在该实例界面停止。
    /// </summary>
    /// <param name="run">运行视图</param>
    /// <param name="dispatched">下发跟踪器中的运行，键为运行 ID</param>
    private PuloniaRunStopAccess GetStopAccess(PuloniaTaskRunView run,
        IReadOnlyDictionary<string, PuloniaDispatchedRun> dispatched)
    {
        if (run.IsHistorical || !PuloniaTaskRunClassifier.IsActive(run.Status) || run.Instance == _overview.CurrentInstanceKey)
            return PuloniaRunStopAccess.Local;
        if (dispatched.TryGetValue(run.RunId, out var tracked) && tracked.Instance == run.Instance)
            return tracked.IsDisconnected ? PuloniaRunStopAccess.Disconnected : PuloniaRunStopAccess.Dispatched;
        return PuloniaRunStopAccess.OtherInstance;
    }

    /// <summary>
    /// 读不出的其他实例运行状态的说明；全部可读时为空。
    /// </summary>
    /// <param name="failures">读不出的运行状态文件</param>
    private static string? DescribeStateFailures(IReadOnlyList<PuloniaTaskFileLoadFailure> failures)
        => failures.Count == 0
            ? null
            : $"有 {failures.Count} 个实例的运行状态无法读取，已跳过这些实例排队和运行中的记录（文件未改动）："
              + string.Join("；", failures.Select(failure =>
                  PuloniaInstanceKey.DescribeDirectory(Path.GetFileName(Path.GetDirectoryName(failure.Path)) ?? "") + "：" + failure.Message));

    /// <summary>
    /// 在 UI 线程按计划范围同步列表，并按运行、节点地址、尝试次数恢复选择。
    /// </summary>
    private void ApplyPlanFilter(string? preferredRunId = null)
    {
        var selectedRunId = preferredRunId ?? SelectedRun?.RunId;
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
        // 有子实例的运行记录时每条都标明执行实例；只有主实例的运行时不显示
        ShowInstanceBadges = filtered.Any(item => item.Run.Instance != PuloniaInstanceKey.Primary);
        // 未选中也是有效状态；后台刷新和计划切换不得强制选回第一条历史。
        // 只有显式提交/续跑指定请求，或保留用户正在查看的请求时才恢复选择。
        SelectedRun = selectedRunId is { } runId
            ? Runs.FirstOrDefault(item => item.RunId == runId)
            : null;
        if (SelectedRun is { } current && current.RunId == selectedRunId)
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
        // 默认选中最近一个失败或需要核对的任务，其次是最后一个任务，打开记录即可看到问题所在
        SelectedRunNodeResult = value?.NodeDetails.LastOrDefault(item =>
                                    item.StatusCategory is PuloniaTaskRunStatusCategory.Failed
                                        or PuloniaTaskRunStatusCategory.Attention)
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

    /// <summary>
    /// 说明文字变化后同步其显隐。
    /// </summary>
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatusMessage));

    /// <summary>
    /// 是否有需要显示的操作结果或错误说明。
    /// </summary>
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
}
