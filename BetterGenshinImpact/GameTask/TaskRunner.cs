using System;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System.Threading;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 用于以独立任务的方式执行任意方法
/// </summary>
public class TaskRunner
{
    private readonly ILogger<BaseTaskThread> _logger = App.GetLogger<BaseTaskThread>();

    private readonly DispatcherTimerOperationEnum _timerOperation = DispatcherTimerOperationEnum.None;

    private readonly string _name = string.Empty;

    public TaskRunner()
    {
    }

    public TaskRunner(DispatcherTimerOperationEnum timerOperation)
    {
        _timerOperation = timerOperation;
    }

    /// <summary>
    /// 加锁并独立运行任务
    /// </summary>
    /// <param name="action"></param>
    /// <returns></returns>
    public async Task RunAsync(Func<Task> action)
    {
        // 加锁
        var hasLock = await TaskSemaphore.WaitAsync(0);
        if (!hasLock)
        {
            _logger.LogError("任务启动失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
            return;
        }

        try
        {
            _logger.LogInformation("→ {Text}", _name + "任务启动！");

            // 初始化
            Init();

            // 发送运行任务通知
            SendNotification();

            await action();
        }
        catch (NormalEndException e)
        {
            _logger.LogInformation("任务中断:{Msg}", e.Message);
            SendNotification();
        }
        catch (Exception e)
        {
            _logger.LogError(e.Message);
            _logger.LogDebug(e.StackTrace);
            SendNotification();
        }
        finally
        {
            End();
            _logger.LogInformation("→ {Text}", _name + "任务结束");
            SendNotification();

            // 释放锁
            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    public void Init()
    {
        if (_timerOperation == DispatcherTimerOperationEnum.StopTimer)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.Stop);
        }
        else if (_timerOperation == DispatcherTimerOperationEnum.UseCacheImage)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.OnlyCacheCapture);
        }
    }

    public void End()
    {
        VisionContext.Instance().DrawContent.ClearAll();
        if (_timerOperation == DispatcherTimerOperationEnum.StopTimer)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.Start);
        }
        else if (_timerOperation == DispatcherTimerOperationEnum.UseCacheImage)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.CacheCaptureWithTrigger);
        }
    }

    public void SendNotification()
    {
    }
}
