using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear;
using BetterGenshinImpact.Model.Gear.Tasks;
using BetterGenshinImpact.ViewModel.Pages.Component;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 齿轮任务执行器，负责从 JSON 数据解析任务并执行
/// </summary>
public partial class GearTaskExecutor : ObservableObject
{
    private readonly ILogger<GearTaskExecutor> _logger;
    private readonly GearTaskStorageService _storageService;
    private readonly GearTaskConverter _taskConverter;
    private readonly GearTaskExecutionManager _executionManager;
    private readonly IServiceProvider _serviceProvider;
    
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
        GearTaskStorageService storageService,
        GearTaskConverter taskConverter,
        GearTaskExecutionManager executionManager,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _storageService = storageService;
        _taskConverter = taskConverter;
        _executionManager = executionManager;
        _serviceProvider = serviceProvider;
        
        // 订阅执行管理器事件
        _executionManager.ProgressChanged += OnProgressChanged;
        _executionManager.PropertyChanged += OnExecutionManagerPropertyChanged;
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

            // 从存储服务加载任务定义
            var taskDefinitionViewModel = await _storageService.LoadTaskDefinitionAsync(taskDefinitionName);
            if (taskDefinitionViewModel == null)
            {
                throw new ArgumentException($"未找到任务定义: {taskDefinitionName}");
            }

            if (taskDefinitionViewModel.RootTask == null)
            {
                throw new InvalidOperationException($"任务定义 '{taskDefinitionName}' 没有根任务");
            }

            _logger.LogInformation("开始执行任务定义: {TaskDefinitionName}", taskDefinitionName);
            
            // 转换为可执行的任务
            var rootTaskData = ConvertViewModelToData(taskDefinitionViewModel.RootTask);
            var rootTask = await _taskConverter.ConvertTaskDataAsync(rootTaskData);
            
            // 使用执行管理器执行任务
            await _executionManager.ExecuteWithTrackingAsync(rootTask, ct);
            
            StatusMessage = "任务执行完成";
            Progress = 100;
            _logger.LogInformation("任务定义执行完成: {TaskDefinitionName}", taskDefinitionName);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "任务执行已取消";
            _logger.LogInformation("任务定义执行已取消: {TaskDefinitionName}", taskDefinitionName);
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"任务执行失败: {ex.Message}";
            _logger.LogError(ex, "执行任务定义时发生错误: {TaskDefinitionName}", taskDefinitionName);
            throw;
        }
        finally
        {
            IsExecuting = false;
        }
    }

    /// <summary>
    /// 直接执行 GearTaskData
    /// </summary>
    /// <param name="taskData">任务数据</param>
    /// <param name="ct">取消令牌</param>
    /// <returns></returns>
    public async Task ExecuteTaskDataAsync(GearTaskData taskData, CancellationToken ct = default)
    {
        if (IsExecuting)
        {
            throw new InvalidOperationException("任务执行器正在运行中，请等待当前任务完成");
        }

        try
        {
            IsExecuting = true;
            StatusMessage = "正在执行任务...";
            Progress = 0;

            _logger.LogInformation("开始执行任务: {TaskName}", taskData.Name);
            
            // 转换为可执行的任务
            var executableTask = await _taskConverter.ConvertTaskDataAsync(taskData);
            
            // 使用执行管理器执行任务
            await _executionManager.ExecuteWithTrackingAsync(executableTask, ct);
            
            StatusMessage = "任务执行完成";
            Progress = 100;
            _logger.LogInformation("任务执行完成: {TaskName}", taskData.Name);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "任务执行已取消";
            _logger.LogInformation("任务执行已取消: {TaskName}", taskData.Name);
            throw;
        }
        catch (Exception ex)
        {
            StatusMessage = $"任务执行失败: {ex.Message}";
            _logger.LogError(ex, "执行任务时发生错误: {TaskName}", taskData.Name);
            throw;
        }
        finally
        {
            IsExecuting = false;
        }
    }





    /// <summary>
    /// 停止当前执行的任务
    /// </summary>
    public void StopExecution()
    {
        if (IsExecuting)
        {
            StatusMessage = "正在停止任务执行...";
            _logger.LogInformation("用户请求停止任务执行");
            _executionManager.CancelExecution();
        }
    }

    /// <summary>
    /// 处理进度变化事件
    /// </summary>
    /// <param name="sender">事件发送者</param>
    /// <param name="progress">进度值</param>
    private void OnProgressChanged(object? sender, double progress)
    {
        Progress = progress;
    }

    /// <summary>
    /// 处理执行管理器属性变化事件
    /// </summary>
    /// <param name="sender">事件发送者</param>
    /// <param name="e">属性变化事件参数</param>
    private void OnExecutionManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GearTaskExecutionManager.CurrentTaskInfo):
                if (_executionManager.CurrentTaskInfo != null)
                {
                    CurrentTaskName = _executionManager.CurrentTaskInfo.TaskName;
                }
                break;
            case nameof(GearTaskExecutionManager.OverallStatusMessage):
                StatusMessage = _executionManager.OverallStatusMessage;
                break;
            case nameof(GearTaskExecutionManager.IsExecuting):
                IsExecuting = _executionManager.IsExecuting;
                break;
        }
    }

    /// <summary>
    /// 获取执行统计信息
    /// </summary>
    /// <returns>统计信息</returns>
    public TaskExecutionStatistics GetExecutionStatistics()
    {
        return _executionManager.GetStatistics();
    }

    /// <summary>
    /// 获取当前任务执行信息
    /// </summary>
    /// <returns>当前任务信息</returns>
    public TaskExecutionInfo? GetCurrentTaskInfo()
    {
        return _executionManager.CurrentTaskInfo;
    }

    /// <summary>
    /// 获取根任务执行信息
    /// </summary>
    /// <returns></returns>
    public TaskExecutionInfo? GetRootTaskInfo()
    {
        return _executionManager.RootTaskInfo;
    }

    /// <summary>
    /// 将 GearTaskViewModel 转换为 GearTaskData
    /// </summary>
    /// <param name="viewModel">视图模型</param>
    /// <returns>任务数据</returns>
    private GearTaskData ConvertViewModelToData(GearTaskViewModel viewModel)
    {
        var taskData = new GearTaskData
        {
            Name = viewModel.Name,
            TaskType = viewModel.TaskType,
            IsEnabled = viewModel.IsEnabled,
            IsDirectory = viewModel.IsDirectory,
            Parameters = viewModel.Parameters,
            CreatedTime = viewModel.CreatedTime,
            ModifiedTime = viewModel.ModifiedTime,
            Priority = viewModel.Priority,
        };

        // 递归转换子任务
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

/// <summary>
/// 空任务实现，用于已禁用的任务
/// </summary>
internal class EmptyGearTask : BaseGearTask
{
    public override Task Run(CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}