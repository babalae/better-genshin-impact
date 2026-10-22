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
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Helpers;
using Wpf.Ui.Violeta.Controls;

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

    /// <summary>
    /// 游戏运行环境的编排服务，用于按需启动截图器。TaskRunner 有多处直接 new，暂时从容器获取
    /// </summary>
    private readonly GameRuntimeService _gameRuntimeService = App.GetService<GameRuntimeService>()!;

    public TaskRunner()
    {
    }

    /// <summary>
    /// 加锁并使用本轮独立取消令牌运行任务。
    /// </summary>
    /// <param name="action">接收本轮运行环境与取消令牌的任务操作。任务只使用这个运行环境，不查询"当前"</param>
    /// <param name="waitForMainUi">本轮新启动截图器时是否等待进入可执行任务的游戏界面；截图器已在运行时不等待。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    /// <param name="taskName">写入日志的任务名称，可为空。</param>
    public async Task RunCurrentAsync(Func<GameRuntime, CancellationToken, Task> action, bool waitForMainUi = true,
        CancellationToken requestToken = default, string? taskName = null)
    {
        _ = await RunCurrentWithResultAsync(action, waitForMainUi, requestToken, taskName);
    }

    /// <summary>
    /// 加锁运行任意任务操作，并返回明确的结束原因。
    /// </summary>
    /// <param name="action">接收本轮运行环境与取消令牌的任务操作。</param>
    /// <param name="waitForMainUi">本轮新启动截图器时是否等待进入可执行任务的游戏界面；截图器已在运行时不等待。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    /// <param name="taskName">写入日志的任务名称，可为空。</param>
    public Task<SoloTaskRunResult> RunCurrentWithResultAsync(
        Func<GameRuntime, CancellationToken, Task> action,
        bool waitForMainUi = true,
        CancellationToken requestToken = default,
        string? taskName = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RunCoreAsync(async (runtime, ct) =>
        {
            await action(runtime, ct);
            return null;
        }, waitForMainUi, taskName, requestToken);
    }

    /// <summary>
    /// 在后台启动任务且不等待完成。
    /// </summary>
    /// <param name="action">接收本轮运行环境与取消令牌的任务操作。</param>
    /// <param name="waitForMainUi">本轮新启动截图器时是否等待进入可执行任务的游戏界面；截图器已在运行时不等待。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    public void FireAndForget(Func<GameRuntime, CancellationToken, Task> action, bool waitForMainUi = true,
        CancellationToken requestToken = default)
    {
        Task.Run(() => RunCurrentAsync(action, waitForMainUi, requestToken));
    }

    /// <summary>
    /// 在线程池中运行任务操作。
    /// </summary>
    /// <param name="action">接收本轮运行环境与取消令牌的任务操作。</param>
    /// <param name="waitForMainUi">本轮新启动截图器时是否等待进入可执行任务的游戏界面；截图器已在运行时不等待。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    /// <param name="taskName">写入日志的任务名称，可为空。</param>
    public async Task RunThreadAsync(Func<GameRuntime, CancellationToken, Task> action, bool waitForMainUi = true,
        CancellationToken requestToken = default, string? taskName = null)
    {
        await Task.Run(() => RunCurrentAsync(action, waitForMainUi, requestToken, taskName));
    }

    /// <summary>
    /// 运行编译期已知类型的独立任务。任务在拿到本轮运行环境之后才构造，构造期不会读到上一次绑定的旧值。
    /// </summary>
    /// <param name="create">用本轮运行环境构造任务。</param>
    /// <param name="param">本次运行的业务参数。</param>
    /// <param name="waitForMainUi">本轮新启动截图器时是否等待进入可执行任务的游戏界面。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    /// <returns>运行结果，任务正常结束时包含任务结果。</returns>
    public Task<SoloTaskRunResult> RunSoloTaskAsync<TParam, TData>(
        Func<GameRuntime, ISoloTask<TParam, TData>> create,
        TParam param,
        bool waitForMainUi = true,
        CancellationToken requestToken = default)
        where TParam : class, new()
        where TData : class
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(param);
        return Task.Run(() => RunCoreAsync(async (runtime, ct) =>
        {
            var task = create(runtime);
            return await task.ExecuteAsync(param, ct);
        }, waitForMainUi, null, requestToken));
    }

    /// <summary>
    /// 按描述符运行已注册的独立任务，供独立任务运行服务使用。
    /// </summary>
    /// <param name="task">任务描述符。</param>
    /// <param name="param">参数对象，类型必须是描述符的参数类型。</param>
    /// <param name="onStarted">任务构造之后、开始运行之前调用，运行服务据此切换为“运行中”。</param>
    /// <param name="config">配置根；为空时从服务容器解析。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    /// <returns>运行结果，任务正常结束时包含任务结果。</returns>
    public Task<SoloTaskRunResult> RunSoloTaskAsync(
        SoloTaskDescriptor task,
        object param,
        Action? onStarted = null,
        AllConfig? config = null,
        CancellationToken requestToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(param);
        return Task.Run(() => RunCoreAsync(async (runtime, ct) =>
        {
            var instance = task.CreateTask(App.ServiceProvider, runtime, config);
            onStarted?.Invoke();
            return await task.RunAsync(instance, param, ct);
        }, task.WaitForMainUi, task.DisplayName, requestToken));
    }

    /// <summary>
    /// 加锁并使用本轮独立取消令牌运行任务，把异常和任务结果映射为运行结果。
    /// </summary>
    /// <param name="action">接收本轮运行环境与取消令牌的任务操作，返回任务结果或 null。</param>
    /// <param name="waitForMainUi">本轮新启动截图器时是否等待进入可执行任务的游戏界面。</param>
    /// <param name="taskName">写入日志的任务名称，可为空。</param>
    /// <param name="requestToken">调用方附加的取消令牌。</param>
    /// <exception cref="InvalidOperationException">当前异步流已经持有任务会话。</exception>
    private async Task<SoloTaskRunResult> RunCoreAsync(
        Func<GameRuntime, CancellationToken, Task<ISoloTaskResult?>> action,
        bool waitForMainUi,
        string? taskName,
        CancellationToken requestToken)
    {
        // 会话内的代码已经持有任务锁，再次进入只会被拒绝，直接报错便于定位
        if (GameTaskSessionScope.IsActive)
        {
            throw new InvalidOperationException("当前已在任务会话中，不能再通过 TaskRunner 启动任务，请直接调用任务的 RunAsync。");
        }

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

        var name = string.IsNullOrEmpty(taskName) ? string.Empty : taskName;
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
            _logger.LogInformation("→ {Text}", name + "任务启动！");

            // 本轮令牌覆盖游戏启动等待、任务模式和全部业务操作。
            var runtime = await AcquireRuntimeAsync(waitForMainUi, runCancellation.Token);
            runCancellation.Token.ThrowIfCancellationRequested();
            Init(runtime);
            taskRuntime = runtime;
            RunnerContext.Instance.Clear();

            ISoloTaskResult? taskResult;
            using (GameTaskSessionScope.Enter())
            {
                taskResult = await action(runtime, runCancellation.Token);
            }

            result = new SoloTaskRunResult
            {
                Outcome = SoloTaskRunOutcome.Completed,
                ErrorMessage = taskResult is { Kind: SoloTaskResultKind.Failed } ? taskResult.Message : null,
                TaskResult = taskResult
            };
            ReportTaskResult(name, taskResult);
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
                Outcome = SoloTaskRunOutcome.Faulted,
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
                _logger.LogInformation("→ {Text}", name + "任务结束");
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
    /// 记录任务返回的业务结果；失败结果发送任务异常通知，保持与原来抛异常失败时相同的提醒。
    /// </summary>
    /// <param name="name">任务名称。</param>
    /// <param name="taskResult">任务结果，委托运行时为 null。</param>
    private void ReportTaskResult(string name, ISoloTaskResult? taskResult)
    {
        if (taskResult is null)
        {
            return;
        }

        if (taskResult.Kind == SoloTaskResultKind.Failed)
        {
            _logger.LogError("{Name}任务失败：{Message}（{Reason}）", name, taskResult.Message, taskResult.ReasonCode);
            Notify.Event(NotificationEvent.TaskError).Error($"{name}任务失败：{taskResult.Message}");
            return;
        }

        if (!string.IsNullOrEmpty(taskResult.Message))
        {
            _logger.LogInformation("{Name}任务结果：{Kind}，{Message}", name, taskResult.Kind, taskResult.Message);
        }
    }

    /// <summary>
    /// 获取本轮任务使用的运行环境：按需启动截图器 → 本轮新启动时等待进入可执行任务的界面 → 等待订阅脚本自动更新结束
    /// </summary>
    /// <param name="waitForMainUi">本轮新启动截图器时是否等待进入可执行任务的界面；截图器已在运行时不等待</param>
    /// <param name="ct">本轮任务的取消令牌</param>
    /// <returns>本轮任务使用的运行环境</returns>
    /// <exception cref="NormalEndException">截图器无法启动，或等待期间截图器被停止</exception>
    /// <exception cref="TimeoutException">等待进入游戏或等待订阅脚本更新超时</exception>
    private async Task<GameRuntime> AcquireRuntimeAsync(bool waitForMainUi, CancellationToken ct)
    {
        var runtime = _gameRuntimeService.Current;
        if (runtime is null)
        {
            var started = await StartRuntimeOrThrowAsync(ct);
            if (waitForMainUi)
            {
                // 识别是同步的截图与模板匹配，放到线程池执行，避免调用方在 UI 线程时卡住界面
                await Task.Run(() => new WaitForGameReadyTask(started).Start(ct), ct);
            }

            runtime = started;
        }

        // 自动更新会删除并重新检出用户脚本目录，等它结束后再执行任务
        await ScriptRepoUpdater.Instance.WaitForAutoUpdateAsync(ct);
        return runtime;
    }

    /// <summary>
    /// 启动截图器并返回绑定的运行环境；无法启动时提示用户，并按正常中断结束本轮任务
    /// </summary>
    /// <param name="ct">本轮任务的取消令牌</param>
    /// <returns>本次启动绑定的运行环境</returns>
    /// <exception cref="NormalEndException">截图器无法启动</exception>
    private async Task<GameRuntime> StartRuntimeOrThrowAsync(CancellationToken ct)
    {
        var runtime = await _gameRuntimeService.StartAsync(ct);
        ct.ThrowIfCancellationRequested();
        if (runtime is not null)
        {
            return runtime;
        }

        // 未找到窗口、用户取消等原因已由启动流程提示，这里直接结束任务，不再空等
        UIDispatcherHelper.Invoke(() => { Toast.Warning("请先在启动页，启动截图器再使用本功能"); });
        throw new NormalEndException("截图器未启动");
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
