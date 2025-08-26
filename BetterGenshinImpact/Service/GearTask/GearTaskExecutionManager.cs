using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 任务执行状态
/// </summary>
public enum TaskExecutionStatus
{
    /// <summary>
    /// 等待执行
    /// </summary>
    Pending,
    
    /// <summary>
    /// 正在执行
    /// </summary>
    Running,
    
    /// <summary>
    /// 执行完成
    /// </summary>
    Completed,
    
    /// <summary>
    /// 执行失败
    /// </summary>
    Failed,
    
    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled,
    
    /// <summary>
    /// 已跳过
    /// </summary>
    Skipped
}

/// <summary>
/// 任务执行信息
/// </summary>
public partial class TaskExecutionInfo : ObservableObject
{
    [ObservableProperty]
    private string _taskName = string.Empty;
    
    [ObservableProperty]
    private string _taskType = string.Empty;
    
    [ObservableProperty]
    private TaskExecutionStatus _status = TaskExecutionStatus.Pending;
    
    [ObservableProperty]
    private DateTime _startTime;
    
    [ObservableProperty]
    private DateTime _endTime;
    
    [ObservableProperty]
    private TimeSpan _duration;
    
    [ObservableProperty]
    private string _errorMessage = string.Empty;
    
    [ObservableProperty]
    private double _progress;
    
    [ObservableProperty]
    private string _statusMessage = string.Empty;
    
    public BaseGearTask? Task { get; set; }
    public List<TaskExecutionInfo> Children { get; set; } = new();
    public TaskExecutionInfo? Parent { get; set; }
}

/// <summary>
/// 齿轮任务执行管理器，负责管理任务执行状态和进度跟踪
/// </summary>
public partial class GearTaskExecutionManager : ObservableObject
{
    private readonly ILogger<GearTaskExecutionManager> _logger;
    private readonly Dictionary<BaseGearTask, TaskExecutionInfo> _taskInfoMap = new();
    private CancellationTokenSource? _cancellationTokenSource;
    
    [ObservableProperty]
    private TaskExecutionInfo? _rootTaskInfo;
    
    [ObservableProperty]
    private TaskExecutionInfo? _currentTaskInfo;
    
    [ObservableProperty]
    private bool _isExecuting;
    
    [ObservableProperty]
    private double _overallProgress;
    
    [ObservableProperty]
    private string _overallStatusMessage = string.Empty;
    
    [ObservableProperty]
    private int _totalTasks;
    
    [ObservableProperty]
    private int _completedTasks;
    
    [ObservableProperty]
    private int _failedTasks;
    
    [ObservableProperty]
    private int _skippedTasks;

    public event EventHandler<TaskExecutionInfo>? TaskStarted;
    public event EventHandler<TaskExecutionInfo>? TaskCompleted;
    public event EventHandler<TaskExecutionInfo>? TaskFailed;
    public event EventHandler<TaskExecutionInfo>? TaskSkipped;
    public event EventHandler<double>? ProgressChanged;

    public GearTaskExecutionManager(ILogger<GearTaskExecutionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 开始执行任务并跟踪状态
    /// </summary>
    /// <param name="rootTask">根任务</param>
    /// <param name="ct">取消令牌</param>
    /// <returns></returns>
    public async Task ExecuteWithTrackingAsync(BaseGearTask rootTask, CancellationToken ct = default)
    {
        if (IsExecuting)
        {
            throw new InvalidOperationException("任务执行管理器正在运行中");
        }

        try
        {
            IsExecuting = true;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            
            // 初始化执行信息
            InitializeExecutionInfo(rootTask);
            
            // 开始执行
            await ExecuteTaskWithTrackingAsync(rootTask, _cancellationTokenSource.Token);
            
            // 更新最终状态
            UpdateOverallStatus();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("任务执行已取消");
            OverallStatusMessage = "任务执行已取消";
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "任务执行过程中发生错误");
            OverallStatusMessage = $"任务执行失败: {ex.Message}";
            throw;
        }
        finally
        {
            IsExecuting = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    /// <summary>
    /// 取消任务执行
    /// </summary>
    public void CancelExecution()
    {
        if (IsExecuting && _cancellationTokenSource != null)
        {
            _logger.LogInformation("用户请求取消任务执行");
            _cancellationTokenSource.Cancel();
            OverallStatusMessage = "正在取消任务执行...";
        }
    }

    /// <summary>
    /// 初始化执行信息
    /// </summary>
    /// <param name="rootTask">根任务</param>
    private void InitializeExecutionInfo(BaseGearTask rootTask)
    {
        _taskInfoMap.Clear();
        CompletedTasks = 0;
        FailedTasks = 0;
        SkippedTasks = 0;
        OverallProgress = 0;
        
        RootTaskInfo = CreateTaskExecutionInfo(rootTask);
        TotalTasks = CountTotalTasks(rootTask);
        
        OverallStatusMessage = "准备执行任务...";
        
        _logger.LogInformation("初始化任务执行跟踪，总任务数: {TotalTasks}", TotalTasks);
    }

    /// <summary>
    /// 创建任务执行信息
    /// </summary>
    /// <param name="task">任务</param>
    /// <param name="parent">父任务信息</param>
    /// <returns></returns>
    private TaskExecutionInfo CreateTaskExecutionInfo(BaseGearTask task, TaskExecutionInfo? parent = null)
    {
        var info = new TaskExecutionInfo
        {
            TaskName = task.Name,
            TaskType = task.Type,
            Status = TaskExecutionStatus.Pending,
            Task = task,
            Parent = parent
        };
        
        _taskInfoMap[task] = info;
        
        // 递归创建子任务信息
        if (task.Children?.Count > 0)
        {
            foreach (var child in task.Children)
            {
                var childInfo = CreateTaskExecutionInfo(child, info);
                info.Children.Add(childInfo);
            }
        }
        
        return info;
    }

    /// <summary>
    /// 执行任务并跟踪状态
    /// </summary>
    /// <param name="task">要执行的任务</param>
    /// <param name="ct">取消令牌</param>
    /// <returns></returns>
    private async Task ExecuteTaskWithTrackingAsync(BaseGearTask task, CancellationToken ct)
    {
        if (!_taskInfoMap.TryGetValue(task, out var taskInfo))
        {
            _logger.LogWarning("未找到任务执行信息: {TaskName}", task.Name);
            return;
        }

        CurrentTaskInfo = taskInfo;
        
        // 检查任务是否启用
        if (!task.Enabled)
        {
            await MarkTaskSkipped(taskInfo, "任务已禁用");
            return;
        }

        try
        {
            // 开始执行任务
            await StartTask(taskInfo);
            
            // 执行当前任务
            await task.Execute(ct);
            
            // 执行子任务
            if (task.Children?.Count > 0)
            {
                for (int i = 0; i < task.Children.Count; i++)
                {
                    var child = task.Children[i];
                    await ExecuteTaskWithTrackingAsync(child, ct);
                    
                    // 更新进度
                    var childProgress = (double)(i + 1) / task.Children.Count * 100;
                    taskInfo.Progress = childProgress;
                    UpdateOverallProgress();
                }
            }
            
            // 完成任务
            await CompleteTask(taskInfo);
        }
        catch (OperationCanceledException)
        {
            await MarkTaskCancelled(taskInfo);
            throw;
        }
        catch (Exception ex)
        {
            await FailTask(taskInfo, ex);
            throw;
        }
    }

    /// <summary>
    /// 开始执行任务
    /// </summary>
    /// <param name="taskInfo">任务信息</param>
    private async Task StartTask(TaskExecutionInfo taskInfo)
    {
        taskInfo.Status = TaskExecutionStatus.Running;
        taskInfo.StartTime = DateTime.Now;
        taskInfo.StatusMessage = "正在执行...";
        
        _logger.LogInformation("开始执行任务: {TaskName}", taskInfo.TaskName);
        
        TaskStarted?.Invoke(this, taskInfo);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 完成任务
    /// </summary>
    /// <param name="taskInfo">任务信息</param>
    private async Task CompleteTask(TaskExecutionInfo taskInfo)
    {
        taskInfo.Status = TaskExecutionStatus.Completed;
        taskInfo.EndTime = DateTime.Now;
        taskInfo.Duration = taskInfo.EndTime - taskInfo.StartTime;
        taskInfo.Progress = 100;
        taskInfo.StatusMessage = "执行完成";
        
        CompletedTasks++;
        
        _logger.LogInformation("任务执行完成: {TaskName}, 耗时: {Duration}ms", 
            taskInfo.TaskName, taskInfo.Duration.TotalMilliseconds);
        
        TaskCompleted?.Invoke(this, taskInfo);
        UpdateOverallProgress();
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// 任务执行失败
    /// </summary>
    /// <param name="taskInfo">任务信息</param>
    /// <param name="exception">异常</param>
    private async Task FailTask(TaskExecutionInfo taskInfo, Exception exception)
    {
        taskInfo.Status = TaskExecutionStatus.Failed;
        taskInfo.EndTime = DateTime.Now;
        taskInfo.Duration = taskInfo.EndTime - taskInfo.StartTime;
        taskInfo.ErrorMessage = exception.Message;
        taskInfo.StatusMessage = $"执行失败: {exception.Message}";
        
        FailedTasks++;
        
        _logger.LogError(exception, "任务执行失败: {TaskName}", taskInfo.TaskName);
        
        TaskFailed?.Invoke(this, taskInfo);
        UpdateOverallProgress();
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// 标记任务为已跳过
    /// </summary>
    /// <param name="taskInfo">任务信息</param>
    /// <param name="reason">跳过原因</param>
    private async Task MarkTaskSkipped(TaskExecutionInfo taskInfo, string reason)
    {
        taskInfo.Status = TaskExecutionStatus.Skipped;
        taskInfo.StatusMessage = $"已跳过: {reason}";
        
        SkippedTasks++;
        
        _logger.LogInformation("任务已跳过: {TaskName}, 原因: {Reason}", taskInfo.TaskName, reason);
        
        TaskSkipped?.Invoke(this, taskInfo);
        UpdateOverallProgress();
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// 标记任务为已取消
    /// </summary>
    /// <param name="taskInfo">任务信息</param>
    private async Task MarkTaskCancelled(TaskExecutionInfo taskInfo)
    {
        taskInfo.Status = TaskExecutionStatus.Cancelled;
        taskInfo.EndTime = DateTime.Now;
        taskInfo.Duration = taskInfo.EndTime - taskInfo.StartTime;
        taskInfo.StatusMessage = "已取消";
        
        _logger.LogInformation("任务已取消: {TaskName}", taskInfo.TaskName);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// 更新整体进度
    /// </summary>
    private void UpdateOverallProgress()
    {
        if (TotalTasks > 0)
        {
            var processedTasks = CompletedTasks + FailedTasks + SkippedTasks;
            OverallProgress = (double)processedTasks / TotalTasks * 100;
            
            OverallStatusMessage = $"进度: {processedTasks}/{TotalTasks} (完成: {CompletedTasks}, 失败: {FailedTasks}, 跳过: {SkippedTasks})";
            
            ProgressChanged?.Invoke(this, OverallProgress);
        }
    }

    /// <summary>
    /// 更新最终状态
    /// </summary>
    private void UpdateOverallStatus()
    {
        if (FailedTasks > 0)
        {
            OverallStatusMessage = $"执行完成，但有 {FailedTasks} 个任务失败";
        }
        else if (SkippedTasks > 0)
        {
            OverallStatusMessage = $"执行完成，跳过了 {SkippedTasks} 个任务";
        }
        else
        {
            OverallStatusMessage = "所有任务执行完成";
        }
    }

    /// <summary>
    /// 统计任务总数
    /// </summary>
    /// <param name="task">根任务</param>
    /// <returns>任务总数</returns>
    private int CountTotalTasks(BaseGearTask task)
    {
        int count = 1;
        if (task.Children?.Count > 0)
        {
            foreach (var child in task.Children)
            {
                count += CountTotalTasks(child);
            }
        }
        return count;
    }

    /// <summary>
    /// 获取任务执行统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    public TaskExecutionStatistics GetStatistics()
    {
        return new TaskExecutionStatistics
        {
            TotalTasks = TotalTasks,
            CompletedTasks = CompletedTasks,
            FailedTasks = FailedTasks,
            SkippedTasks = SkippedTasks,
            OverallProgress = OverallProgress,
            IsExecuting = IsExecuting
        };
    }
}

/// <summary>
/// 任务执行统计信息
/// </summary>
public class TaskExecutionStatistics
{
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int FailedTasks { get; set; }
    public int SkippedTasks { get; set; }
    public double OverallProgress { get; set; }
    public bool IsExecuting { get; set; }
    
    public int ProcessedTasks => CompletedTasks + FailedTasks + SkippedTasks;
    public double SuccessRate => TotalTasks > 0 ? (double)CompletedTasks / TotalTasks * 100 : 0;
}