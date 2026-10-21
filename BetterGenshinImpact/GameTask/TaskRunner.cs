using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

using BetterGenshinImpact.View;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Runtime;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using BetterGenshinImpact.GameTask.SoloTask;

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

    /// <summary>
    /// 独立任务运行期间的 Windows 通知抑制服务。
    /// </summary>
    private readonly WindowsNotificationSuppressionService _windowsNotificationSuppressionService =
        App.GetService<WindowsNotificationSuppressionService>()!;

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
    /// <param name="action">接收本轮运行环境与取消令牌的任务操作。任务只使用这个运行环境，不查询"当前"</param>
    /// <param name="waitForMainUi">启动游戏运行环境后是否等待进入可执行任务的游戏界面。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public async Task RunCurrentAsync(Func<GameRuntime, CancellationToken, Task> action, bool waitForMainUi = true,
        CancellationToken requestToken = default)
    {
        _ = await RunCurrentWithResultAsync(action, waitForMainUi, requestToken);
    }

    /// <summary>
    /// 加锁运行独立任务，并向多实例运行协调器返回明确的结束原因。
    /// </summary>
    /// <param name="action">接收本轮运行环境与取消令牌的任务操作。</param>
    /// <param name="waitForMainUi">启动游戏运行环境后是否等待进入可执行任务的游戏界面。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public async Task<SoloTaskRunResult> RunCurrentWithResultAsync(
        Func<GameRuntime, CancellationToken, Task> action,
        bool waitForMainUi = true,
        CancellationToken requestToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        // 加锁
        var hasLock = await TaskSemaphore.WaitAsync(0);
        if (!hasLock)
        {
            _logger.LogError("任务启动失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
            return new SoloTaskRunResult
            {
                Outcome = SoloTaskRunOutcome.Rejected,
                ErrorMessage = "当前实例存在正在运行中的独立任务"
            };
        }

        var runCancellation = requestToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(requestToken)
            : new CancellationTokenSource();
        var stopReasonValue = -1;
        IDisposable? stopRegistration = null;
        IDisposable? notificationSuppression = null;
        var result = new SoloTaskRunResult { Outcome = SoloTaskRunOutcome.Completed };
        // 进入任务模式后才有值，收尾时据此退出任务模式
        GameRuntime? taskRuntime = null;
        try
        {
            // 成功取得独立任务锁后再修改系统设置，避免启动请求被拒绝时短暂切换勿扰。
            notificationSuppression = _windowsNotificationSuppressionService.TrySuppressForSoloTask();
            stopRegistration = _taskStopService.Register(reason =>
            {
                Interlocked.CompareExchange(ref stopReasonValue, (int)reason, -1);
                TryCancel(runCancellation);
            });
            _logger.LogInformation("→ {Text}", _name + "任务启动！");

            // 本轮令牌覆盖游戏启动等待、任务模式和全部业务操作。
            var runtime = await ScriptService.StartGameTask(runCancellation.Token, waitForMainUi);
            runCancellation.Token.ThrowIfCancellationRequested();
            Init(runtime);
            taskRuntime = runtime;
            RunnerContext.Instance.Clear();

            await action(runtime, runCancellation.Token);
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

            result = new SoloTaskRunResult
            {
                Outcome = runCancellation.IsCancellationRequested
                    ? SoloTaskRunOutcome.Cancelled
                    : SoloTaskRunOutcome.Completed
            };
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

            result = new SoloTaskRunResult { Outcome = SoloTaskRunOutcome.Cancelled };
        }
        catch (Exception e)
        {
            Notify.Event(NotificationEvent.TaskError).Error("任务执行异常", e);
            _logger.LogError(e.Message);
            _logger.LogDebug(e.StackTrace);
            result = new SoloTaskRunResult
            {
                Outcome = SoloTaskRunOutcome.Failed,
                ErrorMessage = e.GetBaseException().Message
            };
        }
        finally
        {
            try
            {
                if (taskRuntime is not null)
                {
                    End(taskRuntime);
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
                    try
                    {
                        notificationSuppression?.Dispose();
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

        return result;
    }

    /// <summary>
    /// 在后台启动任务且不等待完成。
    /// </summary>
    /// <param name="action">接收本轮运行环境与取消令牌的任务操作。</param>
    /// <param name="waitForMainUi">是否等待进入可执行任务的游戏界面。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public void FireAndForget(Func<GameRuntime, CancellationToken, Task> action, bool waitForMainUi = true,
        CancellationToken requestToken = default)
    {
        Task.Run(() => RunCurrentAsync(action, waitForMainUi, requestToken));
    }

    /// <summary>
    /// 在线程池中运行独立任务。
    /// </summary>
    /// <param name="action">接收本轮运行环境与取消令牌的任务操作。</param>
    /// <param name="waitForMainUi">是否等待进入可执行任务的游戏界面。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public async Task RunThreadAsync(Func<GameRuntime, CancellationToken, Task> action, bool waitForMainUi = true,
        CancellationToken requestToken = default)
    {
        await Task.Run(() => RunCurrentAsync(action, waitForMainUi, requestToken));
    }

    /// <summary>
    /// 使用独立任务入口运行一个 <see cref="ISoloTask"/>。任务在拿到本轮运行环境之后才构造，构造期不会读到上一次绑定的旧值
    /// </summary>
    /// <param name="create">用本轮运行环境构造独立任务。</param>
    /// <param name="waitForMainUi">启动游戏运行环境后是否等待进入可执行任务的游戏界面。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public Task RunSoloTaskAsync(Func<GameRuntime, ISoloTask> create, bool waitForMainUi = true,
        CancellationToken requestToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        return RunThreadAsync((runtime, ct) => create(runtime).Start(ct), waitForMainUi, requestToken);
    }

    /// <summary>
    /// 使用独立任务入口运行任务并返回明确的结束原因。
    /// </summary>
    /// <param name="create">用本轮运行环境构造独立任务。</param>
    /// <param name="waitForMainUi">是否等待进入可执行任务的游戏界面。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public Task<SoloTaskRunResult> RunSoloTaskWithResultAsync(
        Func<GameRuntime, ISoloTask> create,
        bool waitForMainUi = true,
        CancellationToken requestToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        return Task.Run(() => RunCurrentWithResultAsync(
            (runtime, ct) => create(runtime).Start(ct),
            waitForMainUi,
            requestToken));
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

    /// <summary>
    /// 进入任务模式：清理遮罩并激活游戏窗口
    /// </summary>
    /// <param name="runtime">本轮运行环境</param>
    private static void Init(GameRuntime runtime)
    {
        // 运行环境在启动等待期间被停止时，本轮任务直接结束
        runtime.ThrowIfStopping();

        // 进入任务模式：用户开启的实时触发器在下一帧停用，任务期间只运行任务或脚本通过 AddTrigger 启用的触发器
        TaskTriggerDispatcher.Instance().BeginTask();

        // 地图遮罩触发器停用时也会复位，但要等下一帧；这里同步复位，保证任务第一次点击之前恢复点击穿透
        // 清空绘制内容
        runtime.MaskWindowMapState.Reset();
        runtime.MaskWindowDrawingBoard.ClearAll();

        // 激活原神窗口；遮罩的显示由下一帧上报给 IMaskWindowHost 后自动恢复
        runtime.Window.Activate();
    }

    /// <summary>
    /// 退出任务模式：释放按键、清理遮罩
    /// </summary>
    /// <param name="runtime">本轮运行环境</param>
    private static void End(GameRuntime runtime)
    {
        // 退出任务模式，下一帧按用户配置恢复实时触发器。
        // 放在最前面：即使截图器已在任务中停止，也要保证下次启动后不再停留在任务模式
        TaskTriggerDispatcher.InstanceNullable()?.EndTask();

        // 运行环境已在任务中停止：按键与遮罩已由解绑流程清理
        if (runtime.IsStopping)
        {
            return;
        }

        InputHub.ReleaseAll();

        runtime.MaskWindowDrawingBoard.ClearAll();
        HtmlMaskWindow.CloseAll();
    }

}
