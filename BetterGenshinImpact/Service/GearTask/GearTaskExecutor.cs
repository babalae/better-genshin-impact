using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Model.Gear;
using BetterGenshinImpact.Model.Gear.Tasks;
using BetterGenshinImpact.Service.GearTask;
using BetterGenshinImpact.Service.GearTask.Execution;
using BetterGenshinImpact.ViewModel.Pages.Component;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 齿轮任务执行入口，负责加载任务定义、构造执行上下文并启动执行。
/// </summary>
public partial class GearTaskExecutor : ObservableObject, IGearTaskEventConsumer, IDisposable
{
    private readonly ILogger<GearTaskExecutor> _logger;
    private readonly BetterGenshinImpact.Service.GearTask.GearTaskStorageService _storageService;
    private readonly GearTaskConverter _taskConverter;
    private readonly IGearTaskExecutionRunner _executionRunner;
    private readonly IGearTaskHistoryStore _historyStore;
    private readonly IDisposable _subscription;
    private CancellationTokenSource? _runningCancellationTokenSource;
    private string? _activeRecordId;
    private int _totalNodeCount;
    private int _processedNodeCount;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    private string _currentTaskName = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public GearTaskExecutor(
        ILogger<GearTaskExecutor> logger,
        BetterGenshinImpact.Service.GearTask.GearTaskStorageService storageService,
        GearTaskConverter taskConverter,
        IGearTaskExecutionRunner executionRunner,
        IGearTaskHistoryStore historyStore,
        IGearTaskEventBus eventBus)
    {
        _logger = logger;
        _storageService = storageService;
        _taskConverter = taskConverter;
        _executionRunner = executionRunner;
        _historyStore = historyStore;
        _subscription = eventBus.Subscribe(this);
    }

    /// <summary>
    /// 从 JSON 文件加载并执行任务定义
    /// </summary>
    /// <param name="taskDefinitionName">任务定义名称</param>
    /// <param name="ct">取消令牌</param>
    /// <returns></returns>
    public async Task ExecuteTaskDefinitionAsync(string taskDefinitionName, CancellationToken ct = default)
    {
        if (IsExecuting)
        {
            throw new InvalidOperationException("任务执行器正在运行中，请等待当前任务完成");
        }

        try
        {
            IsExecuting = true;
            StatusMessage = "正在加载任务定义...";
            Progress = 0;
            CurrentTaskName = string.Empty;

            var taskDefinitionViewModel = await _storageService.LoadTaskDefinitionAsync(taskDefinitionName);
            if (taskDefinitionViewModel == null)
            {
                throw new ArgumentException($"未找到任务定义 {taskDefinitionName}");
            }

            if (taskDefinitionViewModel.RootTask == null)
            {
                throw new InvalidOperationException($"任务定义 '{taskDefinitionName}' 没有根任务");
            }

            await ExecuteInternalAsync(taskDefinitionViewModel, resumePlan: null, ct);
        }
        finally
        {
            CleanupExecutionState();
        }
    }

    public async Task ResumeTaskDefinitionAsync(string taskDefinitionName, string sourceRecordId, CancellationToken ct = default)
    {
        if (IsExecuting)
        {
            throw new InvalidOperationException("任务执行器正在运行中，请等待当前任务完成");
        }

        try
        {
            IsExecuting = true;
            StatusMessage = "正在加载恢复记录...";
            Progress = 0;
            CurrentTaskName = string.Empty;

            var taskDefinitionViewModel = await _storageService.LoadTaskDefinitionAsync(taskDefinitionName);
            if (taskDefinitionViewModel == null)
            {
                throw new ArgumentException($"未找到任务定义 {taskDefinitionName}");
            }

            if (taskDefinitionViewModel.RootTask == null)
            {
                throw new InvalidOperationException($"任务定义 '{taskDefinitionName}' 没有根任务");
            }

            var taskDefinitionFileKey = GetTaskDefinitionFileKey(taskDefinitionViewModel.Name);
            var sourceRecord = await _historyStore.LoadAsync(taskDefinitionFileKey, sourceRecordId);
            if (sourceRecord == null)
            {
                throw new InvalidOperationException($"未找到恢复记录 {sourceRecordId}");
            }

            var resumePlan = BuildResumePlan(sourceRecord);
            await ExecuteInternalAsync(taskDefinitionViewModel, resumePlan, ct);
        }
        finally
        {
            CleanupExecutionState();
        }
    }

    public void StopExecution()
    {
        if (!IsExecuting || _runningCancellationTokenSource == null)
        {
            return;
        }

        StatusMessage = "正在停止任务执行...";
        _logger.LogInformation("用户请求停止 GearTask 执行");
        _runningCancellationTokenSource.Cancel();
    }

    public async ValueTask ConsumeAsync(GearTaskExecutionEvent evt, CancellationToken ct = default)
    {
        if (_activeRecordId == null || evt.RecordId != _activeRecordId)
        {
            return;
        }

        switch (evt)
        {
            case ExecutionStartedEvent started:
                _totalNodeCount = Math.Max(started.TotalRunnableNodeCount, 1);
                _processedNodeCount = 0;
                StatusMessage = "任务执行中...";
                Progress = 0;
                break;
            case TaskNodeStartedEvent:
                CurrentTaskName = evt.TaskName;
                StatusMessage = $"正在执行: {evt.TaskName}";
                break;
            case TaskNodeCompletedEvent:
            case TaskNodeFailedEvent:
            case TaskNodeSkippedEvent:
                _processedNodeCount++;
                Progress = Math.Min(100, (double)_processedNodeCount / _totalNodeCount * 100);
                break;
            case ExecutionCompletedEvent:
                Progress = 100;
                StatusMessage = "任务执行完成";
                break;
            case ExecutionCancelledEvent:
                StatusMessage = "任务执行已取消";
                break;
            case ExecutionFailedEvent failed:
                StatusMessage = $"任务执行失败: {failed.ErrorMessage}";
                break;
            case ExecutionInterruptedEvent interrupted:
                StatusMessage = $"任务执行中断: {interrupted.Reason}";
                break;
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _runningCancellationTokenSource?.Dispose();
    }

    private async Task ExecuteInternalAsync(GearTaskDefinitionViewModel taskDefinitionViewModel, GearTaskResumePlan? resumePlan, CancellationToken ct)
    {
        _logger.LogInformation("开始执行任务定义 {TaskDefinitionName}", taskDefinitionViewModel.Name);

        var rootTaskData = ConvertViewModelToData(taskDefinitionViewModel.RootTask!);
        var rootTask = await _taskConverter.ConvertTaskDataAsync(rootTaskData);
        var recordId = Guid.NewGuid().ToString("N");
        var taskDefinitionFileKey = GetTaskDefinitionFileKey(taskDefinitionViewModel.Name);
        var runContext = new GearTaskExecutionRunContext
        {
            RecordId = recordId,
            TaskDefinitionName = taskDefinitionViewModel.Name,
            TaskDefinitionFileKey = taskDefinitionFileKey,
            ResumePlan = resumePlan,
        };

        _activeRecordId = recordId;
        CancellationContext.Instance.Set();
        _runningCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct, CancellationContext.Instance.GetActiveToken());

        await ScriptService.StartGameTask();
        await _executionRunner.RunAsync(rootTask, runContext, _runningCancellationTokenSource.Token);
    }

    private void CleanupExecutionState()
    {
        CancellationContext.Instance.Clear();
        RunnerContext.Instance.Clear();
        _runningCancellationTokenSource?.Dispose();
        _runningCancellationTokenSource = null;
        _activeRecordId = null;
        _totalNodeCount = 0;
        _processedNodeCount = 0;
        IsExecuting = false;
    }

    private static GearTaskResumePlan BuildResumePlan(GearTaskExecutionRecord sourceRecord)
    {
        return new GearTaskResumePlan
        {
            SourceRecordId = sourceRecord.RecordId,
            ResumeNodeId = sourceRecord.ResumeNodeId ?? "0",
            NodeResumeTokens = sourceRecord.Nodes
                .Where(n => !string.IsNullOrWhiteSpace(n.ResumeTokenJson))
                .ToDictionary(n => n.NodeId, n => n.ResumeTokenJson!),
        };
    }

    private static string GetTaskDefinitionFileKey(string name)
    {
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        var safeName = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(safeName) ? "unnamed_task" : safeName;
    }

    private GearTaskData ConvertViewModelToData(GearTaskViewModel viewModel)
    {
        var taskData = new GearTaskData
        {
            Name = viewModel.Name,
            TaskType = viewModel.TaskType,
            Path = viewModel.Path,
            IsEnabled = viewModel.IsEnabled,
            IsDirectory = viewModel.IsDirectory,
            GroupConfigJson = GearTaskGroupConfigHelper.Serialize(viewModel.GroupConfig),
            Parameters = viewModel.Parameters,
            CreatedTime = viewModel.CreatedTime,
            ModifiedTime = viewModel.ModifiedTime,
            Priority = viewModel.Priority,
        };

        if (viewModel.Children?.Count > 0)
        {
            taskData.Children = new List<GearTaskData>();
            foreach (var child in viewModel.Children)
            {
                taskData.Children.Add(ConvertViewModelToData(child));
            }
        }

        return taskData;
    }
}
