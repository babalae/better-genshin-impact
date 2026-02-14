using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.ViewModel;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 用于以独立任务的方式执行任意方法
/// </summary>
public class TaskRunner
{
    private readonly ILogger<TaskRunner> _logger = App.GetLogger<TaskRunner>();

    // private readonly DispatcherTimerOperationEnum _timerOperation = DispatcherTimerOperationEnum.None;

    private readonly string _name = string.Empty;

    public TaskRunner()
    {
    }

    // public TaskRunner(DispatcherTimerOperationEnum timerOperation)
    // {
    //     _timerOperation = timerOperation;
    // }
    
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
            
            CancellationContext.Instance.Set();
            RunnerContext.Instance.Clear();

            await action();
        }
        catch (NormalEndException e)
        {
            Notify.Event(NotificationEvent.TaskCancel).Success("任务手动取消，或正常结束");
            _logger.LogInformation("任务中断:{Msg}", e.Message);
            if (RunnerContext.Instance.IsContinuousRunGroup)
            {
                // 连续执行时，抛出异常，终止执行
                throw;
            }
        }
        catch (TaskCanceledException e)
        {
            Notify.Event(NotificationEvent.TaskCancel).Success("任务被手动取消");
            _logger.LogInformation("任务中断:{Msg}", "任务被取消");
            if (RunnerContext.Instance.IsContinuousRunGroup)
            {
                // 连续执行时，抛出异常，终止执行
                throw;
            }
        }
        catch (Exception e)
        {
            Notify.Event(NotificationEvent.TaskError).Error("任务执行异常", e);
            _logger.LogError(e.Message);
            _logger.LogDebug(e.StackTrace);
        }
        finally
        {
            End();
            _logger.LogInformation("→ {Text}", _name + "任务结束");

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
        bool waitForMainUi = soloTask.Name != "自动七圣召唤" && !soloTask.Name.Contains("自动音游") && !soloTask.Name.Contains("幽境危战");
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

        // 清空实时任务触发器
        TaskTriggerDispatcher.Instance().ClearTriggers();
        
        // 隐藏地图遮罩
        UIDispatcherHelper.Invoke(() =>
        {
            if (MaskWindow.InstanceNullable() != null)
            {
                if (MaskWindow.Instance().DataContext is MaskWindowViewModel vm)
                {
                    vm.IsInBigMapUi = false;
                }
            }
        });
        VisionContext.Instance().DrawContent.ClearAll(); 
        
        // 激活原神窗口
        var maskWindow = MaskWindow.Instance();
        SystemControl.ActivateWindow();
        maskWindow.Invoke(maskWindow.Show);
    }

    public void End()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            return;
        }
        
        // 还原实时任务触发器
        TaskTriggerDispatcher.Instance().ClearTriggers();
        TaskTriggerDispatcher.Instance().SetTriggers(GameTaskManager.LoadInitialTriggers());

        VisionContext.Instance().DrawContent.ClearAll();
    }

}
