using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

using BetterGenshinImpact.View;
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
        using var cancellationScope = CancellationContext.Instance.TryJoinOrBeginScope();
        if (cancellationScope == null)
        {
            _logger.LogWarning("当前存在正在执行的任务，请勿重复启动");
            return;
        }

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
        catch (OperationCanceledException)
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
        Task.Run(async () =>
        {
            using var cancellationScope = CancellationContext.Instance.TryBeginScope();
            if (cancellationScope == null)
            {
                _logger.LogWarning("当前存在正在执行的任务，请勿重复启动");
                return;
            }

            await RunCurrentAsync(action);
        });
    }

    public async Task RunThreadAsync(Func<Task> action)
    {
        await Task.Run(() => RunCurrentAsync(action));
    }

    public async Task RunSoloTaskAsync(ISoloTask soloTask)
    {
        // 启动等待也属于本次执行，不能借用其他入口的取消源。
        using var cancellationScope = CancellationContext.Instance.TryBeginScope();
        if (cancellationScope == null)
        {
            _logger.LogWarning("当前存在正在执行的任务，请勿重复启动");
            return;
        }

        // 没启动的时候先启动
        bool waitForMainUi = soloTask.Name != "自动七圣召唤" && !soloTask.Name.Contains("自动音游") &&
                             !soloTask.Name.Contains("幽境危战");
        await ScriptService.StartGameTask(waitForMainUi);
        if (CancellationContext.Instance.IsCancellationRequested)
        {
            _logger.LogInformation("独立任务在启动阶段被取消: {Name}", soloTask.Name);
            return;
        }
        
        await Task.Run(() => RunCurrentAsync(
            async () => await soloTask.Start(cancellationScope.Token)));
    }

    public void Init()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            UIDispatcherHelper.Invoke(() => { Toast.Warning("请先在启动页，启动截图器再使用本功能"); });
            throw new NormalEndException("请先在启动页，启动截图器再使用本功能");
        }

        // 进入任务模式：用户开启的实时触发器在下一帧停用，任务期间只运行任务或脚本通过 AddTrigger 启用的触发器
        TaskTriggerDispatcher.Instance().BeginTask();

        // 地图遮罩触发器停用时也会复位，但要等下一帧；这里同步复位，保证任务第一次点击之前恢复点击穿透
        // 清空绘制内容（上面已确认截图器在运行，当前运行环境一定存在）
        var runtime = TaskContext.Instance().Runtime;
        runtime?.MaskWindowMapState.Reset();
        runtime?.MaskWindowDrawingBoard.ClearAll();

        // 激活原神窗口；遮罩的显示由下一帧上报给 IMaskWindowHost 后自动恢复
        SystemControl.ActivateWindow();
    }

    public void End()
    {
        // 退出任务模式，下一帧按用户配置恢复实时触发器。
        // 放在最前面：即使截图器已在任务中停止，也要保证下次启动后不再停留在任务模式
        TaskTriggerDispatcher.InstanceNullable()?.EndTask();

        if (!TaskContext.Instance().IsInitialized)
        {
            return;
        }

        InputHub.ReleaseAll();

        TaskContext.Instance().Runtime?.MaskWindowDrawingBoard.ClearAll();
        HtmlMaskWindow.CloseAll();
    }

}
