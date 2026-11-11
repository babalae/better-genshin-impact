using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Pulonia.Services;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 主实例标题栏实例列表中各在线子实例正在运行的 Pulonia 计划（统筹设计 9.7）：读取该实例 state.json 的活动运行（只读、按修改标记缓存），
/// 读不出时退回主实例下发跟踪器中运行中的下发运行。有在线子实例时每 5 秒读取一次，下发运行状态变化时立即读取。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>有在线子实例时读取正在运行的计划的间隔。</summary>
    private static readonly TimeSpan ChildRunRefreshInterval = TimeSpan.FromSeconds(5);

    /// <summary>跨实例运行视图，读取子实例运行状态中的活动运行；未注册时为空，不显示正在运行的计划。</summary>
    private readonly PuloniaRunOverview? _puloniaRunOverview;

    /// <summary>主实例下发运行的跟踪器；子实例运行状态读不出时据此显示，并在下发运行变化时触发读取。</summary>
    private readonly PuloniaRunDispatcher? _puloniaRunDispatcher;

    /// <summary>有在线子实例期间定时读取正在运行的计划；没有在线子实例时停止。</summary>
    private DispatcherTimer? _childRunTimer;

    /// <summary>读取版本号；较早发起、较晚完成的读取结果被丢弃。</summary>
    private int _childRunRefreshVersion;

    /// <summary>主实例订阅下发运行的变化；在构造函数中、首次读取在线子实例之前调用。</summary>
    private void InitializeChildInstanceRuns()
    {
        if (_puloniaRunDispatcher is not null)
            _puloniaRunDispatcher.Changed += OnPuloniaDispatchedRunsChanged;
    }

    /// <summary>解除订阅并停止定时读取；在释放时调用。</summary>
    private void DisposeChildInstanceRuns()
    {
        if (_puloniaRunDispatcher is not null)
            _puloniaRunDispatcher.Changed -= OnPuloniaDispatchedRunsChanged;
        _childRunTimer?.Stop();
    }

    /// <summary>
    /// 在线子实例变化后：有子实例时开始定时读取并立即读取一次，没有时停止。
    /// </summary>
    /// <param name="items">在线子实例展示项</param>
    private void UpdateChildInstanceRunWatch(IReadOnlyList<ConnectedInstanceItem> items)
    {
        if (_puloniaRunOverview is null)
            return;
        if (items.Count == 0)
        {
            _childRunTimer?.Stop();
            return;
        }
        if (_childRunTimer is null && Application.Current?.Dispatcher is { } dispatcher)
        {
            _childRunTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = ChildRunRefreshInterval };
            _childRunTimer.Tick += OnChildRunTimerTick;
        }
        _childRunTimer?.Start();
        _ = RefreshChildInstanceRunsAsync(items);
    }

    /// <summary>定时读取当前在线子实例正在运行的计划。</summary>
    private void OnChildRunTimerTick(object? sender, EventArgs e) => _ = RefreshChildInstanceRunsAsync(ConnectedChildInstances);

    /// <summary>下发运行的状态或连接变化（来自管道后台线程）后切回 UI 线程读取。</summary>
    private void OnPuloniaDispatchedRunsChanged(object? sender, EventArgs e)
        => RunOnUi(() => _ = RefreshChildInstanceRunsAsync(ConnectedChildInstances));

    /// <summary>
    /// 读取各在线子实例正在运行的计划名：优先取该实例 state.json 的活动运行；读不出时取下发跟踪器中运行中、连接正常的下发运行。
    /// 读取失败只记录日志，不显示计划名。
    /// </summary>
    /// <param name="items">在线子实例展示项</param>
    private async Task RefreshChildInstanceRunsAsync(IReadOnlyList<ConnectedInstanceItem> items)
    {
        if (_disposed || _puloniaRunOverview is null || items.Count == 0)
            return;
        var version = ++_childRunRefreshVersion;
        IReadOnlyDictionary<string, PuloniaTaskRunView> active;
        try
        {
            active = await _puloniaRunOverview.ListOtherActiveRunsAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning(ex, "读取子实例的 Pulonia 运行状态失败，标题栏暂不显示正在运行的计划");
            active = new Dictionary<string, PuloniaTaskRunView>();
        }
        if (_disposed || version != _childRunRefreshVersion)
            return;

        var dispatched = _puloniaRunDispatcher?.ListRuns() ?? [];
        foreach (var item in items)
        {
            if (item.InstanceKey is not { } instanceKey)
            {
                item.RunningPlanName = null;
                continue;
            }
            item.RunningPlanName = active.TryGetValue(instanceKey, out var run)
                ? run.PlanName
                : dispatched.FirstOrDefault(tracked => tracked.Instance == instanceKey && !tracked.IsDisconnected
                    && tracked.Status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling
                    && tracked.PlanName.Length > 0)?.PlanName;
        }
    }
}
