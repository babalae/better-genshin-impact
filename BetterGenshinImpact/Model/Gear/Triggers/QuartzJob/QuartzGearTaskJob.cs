using System;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.GearTask;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Logging;
using Quartz;
using Serilog.Context;

namespace BetterGenshinImpact.Model.Gear.Triggers.QuartzJob;

/// <summary>
/// Quartz.NET 任务定义
/// </summary>
[DisallowConcurrentExecution]
[PersistJobDataAfterExecution]
public class QuartzGearTaskJob : IJob
{
    public static readonly JobKey Key = new("gear-task-job", "default-group");
    
    private readonly ILogger<QuartzGearTaskJob> _logger = App.GetLogger<QuartzGearTaskJob>();
    
    public async Task Execute(IJobExecutionContext context)
    {
        var jobDataMap = context.MergedJobDataMap;
        var triggerName = jobDataMap.GetString("TriggerName") ?? "Unknown";
        var triggerId = jobDataMap.GetString("TriggerId") ?? Guid.NewGuid().ToString();
        var taskDefinitionName = jobDataMap.GetString("TaskDefinitionName");
        var correlationId = Guid.NewGuid().ToString();
        
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(taskDefinitionName))
                {
                    _logger.LogWarning("触发器 {TriggerName} 未配置任务定义名称", triggerName);
                    return;
                }

                var shouldInterrupt = jobDataMap.GetBooleanValue("ShouldInterruptOthers");
                if (!await ConfirmExecutionIfGameNotActiveAsync(triggerName))
                {
                    _logger.LogInformation("用户取消执行定时触发任务: {TriggerName}", triggerName);
                    return;
                }

                if (shouldInterrupt)
                {
                    await InterruptOtherJobs(context, triggerId);
                }

                var executor = App.GetRequiredService<GearTaskExecutor>();
                await executor.ExecuteTaskDefinitionAsync(taskDefinitionName, context.CancellationToken);

                _logger.LogInformation("触发器 {TriggerName} 的任务定义执行完成", triggerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行定时任务时发生错误");
                throw;
            }
        }
    }

    /// <summary>
    /// 原神未在前台时，确认是否继续执行定时触发任务。
    /// </summary>
    private async Task<bool> ConfirmExecutionIfGameNotActiveAsync(string triggerName)
    {
        if (IsGenshinImpactForeground())
        {
            return true;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _logger.LogWarning("无法显示定时触发确认对话框，已放弃执行任务: {TriggerName}", triggerName);
            return false;
        }

        await dispatcher.InvokeAsync(ActivateMainWindow);

        var result = await ThemedMessageBox.QuestionAsync(
            $"定时触发任务「{triggerName}」已到执行时间，但当前原神未在前台或尚未启动。\n\n是否执行该任务？\n选择「是」后，任务执行器会继续自动启动/切换到游戏窗口；选择「否」将放弃本次执行。",
            "定时触发任务确认",
            MessageBoxButton.YesNo,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    private bool IsGenshinImpactForeground()
    {
        try
        {
            if (SystemControl.IsGenshinImpactActiveByProcess())
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "按前台进程判断原神焦点状态失败");
        }

        try
        {
            return TaskContext.Instance().IsInitialized && SystemControl.IsGenshinImpactActive();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "按窗口句柄判断原神焦点状态失败");
            return false;
        }
    }

    private static void ActivateMainWindow()
    {
        var mainWindow = Application.Current?.MainWindow;
        if (mainWindow == null)
        {
            return;
        }

        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Activate();
    }

    /// <summary>
    /// 中断其他同类型的定时任务
    /// </summary>
    private async Task InterruptOtherJobs(IJobExecutionContext context, string currentTriggerId)
    {
        try
        {
            var scheduler = context.Scheduler;
            var currentJobKey = context.JobDetail.Key;
            
            // 获取所有正在执行的任务
            var executingJobs = await scheduler.GetCurrentlyExecutingJobs();
            
            foreach (var executingJob in executingJobs)
            {
                // 跳过当前任务
                if (executingJob.JobDetail.Key.Equals(currentJobKey))
                    continue;
                
                // 检查是否是同类型的 GearTaskJob
                if (executingJob.JobDetail.JobType == typeof(QuartzGearTaskJob))
                {
                    var executingTriggerId = executingJob.JobDetail.JobDataMap.GetString("TriggerId");
                    if (executingTriggerId != currentTriggerId)
                    {
                        _logger.LogInformation("中断正在执行的任务: {JobKey}", executingJob.JobDetail.Key);
                        await scheduler.Interrupt(executingJob.JobDetail.Key);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "中断其他任务时发生错误");
        }
    }
}
