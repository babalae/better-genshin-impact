using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 管理同一独立任务在多个在线实例中的启动和停止。
/// </summary>
public sealed partial class SoloTaskInstanceWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// 多实例连接与独立任务操作服务。
    /// </summary>
    private readonly InstanceService _instanceService;

    /// <summary>
    /// 当前窗口管理的任务标识。
    /// </summary>
    private readonly string _taskId;

    /// <summary>
    /// 独立任务注册表，用于显示任务名称。
    /// </summary>
    private readonly SoloTaskRegistry _registry;

    /// <summary>
    /// 第一次启动实例时收集任务专用参数的回调。
    /// </summary>
    private readonly Func<Task<SoloTaskStartDefinition?>> _definitionFactory;

    /// <summary>
    /// 已经离线的实例也保留到窗口关闭，以便明确显示断线结果。
    /// </summary>
    private readonly Dictionary<int, SoloTaskInstanceRowViewModel> _rowsByProcessId = [];

    /// <summary>
    /// 当前窗口第一次启动时固定下来的任务专用参数。
    /// </summary>
    private SoloTaskStartDefinition? _definition;

    /// <summary>
    /// 防止窗口关闭后继续接收实例事件。
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// 任一启动或停止请求未确认时，窗口内所有输入统一关闭。
    /// </summary>
    [ObservableProperty]
    private bool _isOperationPending;

    /// <summary>
    /// 按稳定顺序展示的实例行。
    /// </summary>
    public ObservableCollection<SoloTaskInstanceRowViewModel> Instances { get; } = [];

    /// <summary>
    /// 窗口标题。
    /// </summary>
    public string Title => $"管理 · {_registry.GetDisplayName(_taskId)}";

    /// <summary>
    /// 请求承载此 ViewModel 的窗口关闭。
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 创建实例管理窗口状态并订阅连接、任务状态变化。
    /// </summary>
    /// <param name="instanceService">多实例连接与独立任务操作服务</param>
    /// <param name="registry">独立任务注册表</param>
    /// <param name="taskId">要管理的任务标识</param>
    /// <param name="definitionFactory">第一次启动时收集任务参数的回调</param>
    public SoloTaskInstanceWindowViewModel(
        InstanceService instanceService,
        SoloTaskRegistry registry,
        string taskId,
        Func<Task<SoloTaskStartDefinition?>> definitionFactory)
    {
        _instanceService = instanceService;
        _registry = registry;
        _taskId = taskId;
        _definitionFactory = definitionFactory;
        _instanceService.ConnectedInstancesChanged += OnInstancesChanged;
        _instanceService.SoloTaskStatusesChanged += OnInstancesChanged;
        RefreshInstances();
    }

    /// <summary>
    /// 对空闲实例启动当前任务，或停止此实例中当前任务对应的准确运行标识。
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ToggleInstanceAsync(SoloTaskInstanceRowViewModel? row)
    {
        if (row is null || IsOperationPending || !row.CanOperate)
        {
            return;
        }

        IsOperationPending = true;
        RefreshInstances();
        try
        {
            SoloTaskOperationResponse response;
            if (row.Status.IsActive && row.Status.TaskId == _taskId && row.Status.RunId is Guid runId)
            {
                response = await _instanceService.StopSoloTaskAsync(row.Endpoint, runId);
            }
            else
            {
                _definition ??= await _definitionFactory();
                if (_definition is null)
                {
                    return;
                }

                response = await _instanceService.StartSoloTaskAsync(row.Endpoint, _definition.CreateRequest());
            }

            if (!response.Accepted)
            {
                await ThemedMessageBox.WarningAsync(response.ErrorMessage ?? "目标实例拒绝了本次操作。", "独立任务");
            }
        }
        catch (TimeoutException ex)
        {
            // 请求可能已经到达目标实例，先查询真实状态，再决定是否重新开放此行操作。
            try
            {
                await _instanceService.RefreshSoloTaskStatusAsync(row.Endpoint);
            }
            catch
            {
                // 查询失败时仍保留原始超时提示，连接事件会继续校正在线状态。
            }

            await ThemedMessageBox.WarningAsync($"实例操作等待确认超时：{ex.Message}", "独立任务");
        }
        catch (Exception ex)
        {
            await ThemedMessageBox.ErrorAsync($"实例操作失败：{ex.Message}", "独立任务");
        }
        finally
        {
            IsOperationPending = false;
            RefreshInstances();
        }
    }

    /// <summary>
    /// 关闭实例管理窗口。
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 实例连接或任务状态变化时切回窗口 UI 线程刷新。
    /// </summary>
    private void OnInstancesChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            RefreshInstances();
        }
        else
        {
            dispatcher.BeginInvoke(RefreshInstances);
        }
    }

    /// <summary>
    /// 合并当前在线实例和本窗口已经展示过的离线实例，并刷新每行状态。
    /// </summary>
    private void RefreshInstances()
    {
        if (_disposed)
        {
            return;
        }

        var onlineTargets = _instanceService.GetSoloTaskTargets();
        var onlineProcessIds = onlineTargets.Select(endpoint => endpoint.ProcessId).ToHashSet();
        foreach (var endpoint in onlineTargets)
        {
            if (_rowsByProcessId.ContainsKey(endpoint.ProcessId))
            {
                continue;
            }

            var row = new SoloTaskInstanceRowViewModel(endpoint, _registry.GetDisplayName);
            _rowsByProcessId.Add(endpoint.ProcessId, row);
            Instances.Add(row);
        }

        ApplyStableInstanceOrder();

        foreach (var row in Instances)
        {
            var isOnline = onlineProcessIds.Contains(row.Endpoint.ProcessId);
            var status = isOnline
                ? _instanceService.GetSoloTaskStatus(row.Endpoint)
                : row.Status;
            row.Update(status, _taskId, isOnline, IsOperationPending);
        }
    }

    /// <summary>
    /// 始终按主实例、桌面分身会话、云原神名称排列，离线行也保持同一排序规则。
    /// </summary>
    private void ApplyStableInstanceOrder()
    {
        var orderedRows = _rowsByProcessId.Values
            .OrderBy(row => row.Endpoint.InstanceType switch
            {
                BetterGiInstanceType.Primary => 0,
                BetterGiInstanceType.ChildSession => 1,
                BetterGiInstanceType.WebView => 2,
                _ => 3
            })
            .ThenBy(row => row.Endpoint.InstanceType == BetterGiInstanceType.ChildSession
                ? row.Endpoint.WindowsSessionId.ToString("D10")
                : row.Endpoint.InstanceName ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Endpoint.ProcessId)
            .ToArray();
        if (Instances.SequenceEqual(orderedRows))
        {
            return;
        }

        Instances.Clear();
        foreach (var row in orderedRows)
        {
            Instances.Add(row);
        }
    }

    /// <summary>
    /// 解除后台事件订阅，防止关闭后的窗口被服务长期引用。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _instanceService.ConnectedInstancesChanged -= OnInstancesChanged;
        _instanceService.SoloTaskStatusesChanged -= OnInstancesChanged;
    }
}
