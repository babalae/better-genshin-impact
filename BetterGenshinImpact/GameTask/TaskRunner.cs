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

    /// <summary>
    /// 将停止按钮、全局热键和运行环境关闭转发到本轮局部 CTS。
    /// </summary>
    private readonly TaskStopService _taskStopService = App.GetService<TaskStopService>();

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
    /// 加锁并使用本轮独立取消令牌运行任务。
    /// </summary>
    /// <param name="action">显式接收本轮取消令牌的任务操作。</param>
    /// <param name="waitForMainUi">启动游戏运行环境后是否等待进入可执行任务的游戏界面。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public async Task RunCurrentAsync(Func<CancellationToken, Task> action, bool waitForMainUi = true,
        CancellationToken requestToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        // 加锁
        var hasLock = await TaskSemaphore.WaitAsync(0);
        if (!hasLock)
        {
            _logger.LogError("任务启动失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
            return;
        }

        var runCancellation = requestToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(requestToken)
            : new CancellationTokenSource();
        var stopReasonValue = -1;
        IDisposable? stopRegistration = null;
        var taskModeInitialized = false;
        try
        {
            stopRegistration = _taskStopService.Register(reason =>
            {
                Interlocked.CompareExchange(ref stopReasonValue, (int)reason, -1);
                TryCancel(runCancellation);
            });
            _logger.LogInformation("→ {Text}", _name + "任务启动！");

            // 本轮令牌覆盖游戏启动等待、任务模式和全部业务操作。
            await ScriptService.StartGameTask(runCancellation.Token, waitForMainUi);
            runCancellation.Token.ThrowIfCancellationRequested();
            Init();
            taskModeInitialized = true;
            RunnerContext.Instance.Clear();

            await action(runCancellation.Token);
        }
        catch (NormalEndException e)
        {
            if (!runCancellation.IsCancellationRequested)
            {
                Notify.Event(NotificationEvent.TaskCancel).Success("任务正常结束");
            }
            _logger.LogInformation("任务中断:{Msg}", e.Message);
            if (RunnerContext.Instance.IsContinuousRunGroup)
            {
                // 连续执行时，抛出异常，终止执行
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            var stopReason = Volatile.Read(ref stopReasonValue) >= 0
                ? ((TaskStopReason)Volatile.Read(ref stopReasonValue)).ToString()
                : "调用方取消";
            _logger.LogInformation("任务中断:{Msg}，来源：{Reason}", "任务被取消", stopReason);
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
            try
            {
                if (taskModeInitialized)
                {
                    End();
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "清理独立任务模式时发生异常：{Message}", e.Message);
            }
            finally
            {
                _logger.LogInformation("→ {Text}", _name + "任务结束");
                try
                {
                    RunnerContext.Instance.Clear();
                }
                finally
                {
                    stopRegistration?.Dispose();
                    runCancellation.Dispose();

                    // 释放锁
                    if (hasLock)
                    {
                        TaskSemaphore.Release();
                    }
                }
            }
        }
    }

    /// <summary>
    /// 在后台启动任务且不等待完成。
    /// </summary>
    /// <param name="action">显式接收本轮取消令牌的任务操作。</param>
    /// <param name="waitForMainUi">是否等待进入可执行任务的游戏界面。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public void FireAndForget(Func<CancellationToken, Task> action, bool waitForMainUi = true,
        CancellationToken requestToken = default)
    {
        Task.Run(() => RunCurrentAsync(action, waitForMainUi, requestToken));
    }

    /// <summary>
    /// 在线程池中运行独立任务。
    /// </summary>
    /// <param name="action">显式接收本轮取消令牌的任务操作。</param>
    /// <param name="waitForMainUi">是否等待进入可执行任务的游戏界面。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public async Task RunThreadAsync(Func<CancellationToken, Task> action, bool waitForMainUi = true,
        CancellationToken requestToken = default)
    {
        await Task.Run(() => RunCurrentAsync(action, waitForMainUi, requestToken));
    }

    /// <summary>
    /// 使用独立任务入口运行一个 <see cref="ISoloTask"/>。
    /// </summary>
    /// <param name="soloTask">需要执行的独立任务。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public async Task RunSoloTaskAsync(ISoloTask soloTask, CancellationToken requestToken = default)
    {
        bool waitForMainUi = soloTask.Name != "自动七圣召唤" && !soloTask.Name.Contains("自动音游") &&
                             !soloTask.Name.Contains("幽境危战");
        await RunThreadAsync(ct => soloTask.Start(ct), waitForMainUi, requestToken);
    }

    /// <summary>
    /// 尽力取消本轮 CTS；并发结束导致 CTS 已释放时无需再次处理。
    /// </summary>
    private static void TryCancel(CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 任务已完成并进入资源释放阶段，无需重复取消。
        }
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
