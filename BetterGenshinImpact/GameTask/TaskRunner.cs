using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoMusicGame;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Service;

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
    public async Task RunCurrentAsync(Func<Task> action)
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

            CancellationContext.Instance.Set();
            RunnerContext.Instance.Clear();

            await action();
        }
        catch (NormalEndException e)
        {
            _logger.LogInformation("任务中断:{Msg}", e.Message);
            SendNotification();
            if (RunnerContext.Instance.IsContinuousRunGroup)
            {
                // 连续执行时，抛出异常，终止执行
                throw;
            }
        }
        catch (TaskCanceledException e)
        {
            _logger.LogInformation("任务中断:{Msg}", "任务被取消");
            SendNotification();
            if (RunnerContext.Instance.IsContinuousRunGroup)
            {
                // 连续执行时，抛出异常，终止执行
                throw;
            }
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
            RunnerContext.Instance.Clear();

            // 释放锁
            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    public void FireAndForget(Func<Task> action)
    {
        Task.Run(() => RunCurrentAsync(action));
    }

    public async Task RunThreadAsync(Func<Task> action)
    {
        await Task.Run(() => RunCurrentAsync(action));
    }

    public async Task RunSoloTaskAsync(ISoloTask soloTask)
    {
        // 没启动的时候先启动
        bool waitForMainUi = soloTask.Name != "自动七圣召唤" && !soloTask.Name.Contains("自动音游");
        await ScriptService.StartGameTask(waitForMainUi);
        await Task.Run(() => RunCurrentAsync(async () => await soloTask.Start(CancellationContext.Instance.Cts.Token)));
    }

    public void Init()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            UIDispatcherHelper.Invoke(() => { Toast.Warning("请先在启动页，启动截图器再使用本功能"); });
            throw new NormalEndException("请先在启动页，启动截图器再使用本功能");
        }

        // 激活原神窗口
        var maskWindow = MaskWindow.Instance();
        SystemControl.ActivateWindow();
        maskWindow.Invoke(maskWindow.Show);
        if (_timerOperation == DispatcherTimerOperationEnum.UseSelfCaptureImage)
        {
            Thread.Sleep(TaskContext.Instance().Config.TriggerInterval * 5); // 等待日志窗口被激活
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.Stop);
        }
        else if (_timerOperation == DispatcherTimerOperationEnum.UseCacheImage)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.OnlyCacheCapture);
            Thread.Sleep(TaskContext.Instance().Config.TriggerInterval * 5); // 等待缓存图像
        }
        else if (_timerOperation == DispatcherTimerOperationEnum.UseCacheImageWithTrigger)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.CacheCaptureWithTrigger);
            Thread.Sleep(TaskContext.Instance().Config.TriggerInterval * 5); // 等待缓存图像
        }
        else if (_timerOperation == DispatcherTimerOperationEnum.UseCacheImageWithTriggerEmpty)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.CacheCaptureWithTrigger);
            TaskTriggerDispatcher.Instance().ClearTriggers();
            Thread.Sleep(TaskContext.Instance().Config.TriggerInterval * 5); // 等待缓存图像
        }
    }

    public void End()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            return;
        }

        VisionContext.Instance().DrawContent.ClearAll();
        if (_timerOperation == DispatcherTimerOperationEnum.UseSelfCaptureImage)
        {
            TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.Start);
        }
        else if (_timerOperation is DispatcherTimerOperationEnum.UseCacheImage or DispatcherTimerOperationEnum.UseCacheImageWithTrigger or DispatcherTimerOperationEnum.UseCacheImageWithTriggerEmpty)
        {
            // 还原到原来的模式
            if (TaskContext.Instance().Config.CommonConfig.ScreenshotEnabled || TaskContext.Instance().Config.MacroConfig.CombatMacroEnabled)
            {
                TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.CacheCaptureWithTrigger);
            }
            else
            {
                TaskTriggerDispatcher.Instance().SetCacheCaptureMode(DispatcherCaptureModeEnum.NormalTrigger);
            }

            if (_timerOperation == DispatcherTimerOperationEnum.UseCacheImageWithTriggerEmpty)
            {
                TaskTriggerDispatcher.Instance().SetTriggers(GameTaskManager.LoadInitialTriggers());
            }
        }
    }

    public void SendNotification()
    {
    }
}
