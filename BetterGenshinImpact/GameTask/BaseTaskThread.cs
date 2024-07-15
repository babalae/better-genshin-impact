using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 和触发器的区别：任务不需要持续捕获游戏图像
/// </summary>
public class BaseTaskThread
{
    private readonly ILogger<BaseTaskThread> _logger = App.GetLogger<BaseTaskThread>();

    protected BaseTaskParam _taskParam;

    protected BaseTaskThread(BaseTaskParam taskParam)
    {
        _taskParam = taskParam;
    }

    /// <summary>
    /// 加锁并独立运行任务
    /// </summary>
    /// <param name="useLock"></param>
    /// <returns></returns>
    public async Task StandaloneRunAsync(bool useLock = true)
    {
        // 加锁
        var hasLock = false;
        if (useLock)
        {
            hasLock = await TaskSemaphore.WaitAsync(0);
            if (!hasLock)
            {
                _logger.LogError("{Name} 启动失败：当前存在正在运行中的独立任务，请不要重复执行任务！", _taskParam.Name);
                return;
            }
        }

        try
        {
            _logger.LogInformation("→ {Text}", _taskParam.Name + "启动！");

            // 初始化
            Init();

            // 发送运行任务通知
            SendNotification();

            await OnRunAsync();
        }
        catch (NormalEndException e)
        {
            _logger.LogInformation("{Name} 中断:{Msg}", _taskParam.Name, e.Message);
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
            _logger.LogInformation("→ {Text}", _taskParam.Name + "结束");

            // 释放锁
            if (useLock && hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    /// <summary>
    /// 任务运行逻辑
    /// </summary>
    /// <returns></returns>
    public virtual async Task OnRunAsync()
    {
        await Task.Delay(0);
    }

    public void Init()
    {
        if (_taskParam.TriggerOperation == DispatcherTimerOperationEnum.StopTimer)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.Stop);
        }
        else if (_taskParam.TriggerOperation == DispatcherTimerOperationEnum.UseCacheImage)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.OnlyCacheCapture);
        }
    }

    public void End()
    {
        VisionContext.Instance().DrawContent.ClearAll();
        if (_taskParam.TriggerOperation == DispatcherTimerOperationEnum.StopTimer)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.Start);
        }
        else if (_taskParam.TriggerOperation == DispatcherTimerOperationEnum.UseCacheImage)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.CacheCaptureWithTrigger);
        }
    }

    public void SendNotification()
    {
    }
}
