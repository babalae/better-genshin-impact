using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterGenshinImpact.GameTask.Common;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 用于以独立任务的方式执行任意方法
/// </summary>
public class TaskRunner
{
    private readonly ILogger<TaskRunner> _logger = App.GetLogger<TaskRunner>();

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

            // 激活原神窗口
            SystemControl.ActivateWindow();

            // 初始化
            Init();

            // 发送运行任务通知
            SendNotification();

            CancellationContext.Instance.Set();

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

            CancellationContext.Instance.Clear();

            // 释放锁
            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    public void FireAndForget(Func<Task> action)
    {
        Task.Run(() => RunAsync(action));
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
            Sleep(TaskContext.Instance().Config.TriggerInterval * 5, CancellationContext.Instance.Cts); // 等待缓存图像
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
