using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

using BetterGenshinImpact.View;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
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
    /// <param name="resetCancellationContext">任务开始时是否重建 CancellationContext。</param>
    /// <param name="clearCancellationContextOnLockFailure">获取信号量锁失败时是否清理 CancellationContext。</param>
    /// <param name="propagateExceptions">是否向调用方传播未预期的任务异常。</param>
    /// <returns></returns>
    public async Task RunCurrentAsync(Func<Task> action, bool resetCancellationContext = true, bool clearCancellationContextOnLockFailure = false, bool propagateExceptions = false)
    {
        // 加锁
        var hasLock = await TaskSemaphore.WaitAsync(0);
        if (!hasLock)
        {
            _logger.LogError("任务启动失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
            if (clearCancellationContextOnLockFailure)
            {
                CancellationContext.Instance.Clear();
            }
            TaskRunnerFailurePolicy.ThrowIfLockUnavailable(propagateExceptions);
            return;
        }
        Exception? executionException = null;
        try
        {
            _logger.LogInformation("→ {Text}", _name + "任务启动！");

            // 初始化
            Init();
            if (resetCancellationContext)
            {
                CancellationContext.Instance.Set();
            }
            RunnerContext.Instance.Clear();

            await action();
        }
        catch (NormalEndException e)
        {
            Notify.Event(NotificationEvent.TaskCancel).Success("任务手动取消，或正常结束");
            _logger.LogInformation("任务中断:{Msg}", e.Message);
            executionException = TaskRunnerFailurePolicy.GetTerminationException(
                e,
                RunnerContext.Instance.IsContinuousRunGroup,
                propagateExceptions);
        }
        catch (OperationCanceledException e)
        {
            Notify.Event(NotificationEvent.TaskCancel).Success("任务被手动取消");
            _logger.LogInformation("任务中断:{Msg}", "任务被取消");
            executionException = TaskRunnerFailurePolicy.GetTerminationException(
                e,
                RunnerContext.Instance.IsContinuousRunGroup,
                propagateExceptions);
        }
        catch (Exception e)
        {
            Notify.Event(NotificationEvent.TaskError).Error("任务执行异常", e);
            _logger.LogError(e, "任务执行异常: {Message}", e.Message);
            if (propagateExceptions)
            {
                executionException = e;
            }
        }
        finally
        {
            IReadOnlyList<Exception> cleanupFailures;
            try
            {
                cleanupFailures = TaskRunnerCleanup.RunAll(
                [
                    ("任务资源", End),
                    ("结束日志", () => _logger.LogInformation("→ {Text}", _name + "任务结束")),
                    ("取消上下文", CancellationContext.Instance.Clear),
                    ("运行上下文", RunnerContext.Instance.Clear)
                ],
                LogCleanupFailure);
            }
            finally
            {
                // 信号量必须由最外层 finally 释放，不能被任何清理异常阻断。
                if (hasLock)
                {
                    TaskSemaphore.Release();
                }
            }

            TaskRunnerFailurePolicy.ThrowAfterCleanup(
                executionException,
                cleanupFailures,
                propagateExceptions);
        }
    }

    public void FireAndForget(Func<Task> action)
    {
        Task.Run(() => RunCurrentAsync(action));
    }

    public async Task RunThreadAsync(Func<Task> action, bool propagateExceptions = false)
    {
        await Task.Run(() => RunCurrentAsync(action, propagateExceptions: propagateExceptions));
    }

    public async Task RunSoloTaskAsync(ISoloTask soloTask)
    {
        // 启动等待之前先进行取消操作的初始化，便于在任务开始前终止任务.
        CancellationContext.Instance.Set();

        // 没启动的时候先启动
        bool waitForMainUi = soloTask.Name != "自动七圣召唤" && !soloTask.Name.Contains("自动音游") &&
                             !soloTask.Name.Contains("幽境危战");
        await ScriptService.StartGameTask(waitForMainUi);
        if (CancellationContext.Instance.IsCancellationRequested)
        {
            _logger.LogInformation("独立任务在启动阶段被取消: {Name}", soloTask.Name);
            CancellationContext.Instance.Clear();
            return;
        }
        
        await Task.Run(() => RunCurrentAsync(
            async () => await soloTask.Start(CancellationContext.Instance.Cts.Token),
            resetCancellationContext: false,
            clearCancellationContextOnLockFailure: true));
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

        var cleanupFailures = TaskRunnerCleanup.RunAll(
        [
            ("释放模拟输入", Simulation.ReleaseAllKey),
            ("还原实时任务触发器", () =>
            {
                TaskTriggerDispatcher.Instance().ClearTriggers();
                TaskTriggerDispatcher.Instance().SetTriggers(GameTaskManager.LoadInitialTriggers());
            }),
            ("清理绘制内容", VisionContext.Instance().DrawContent.ClearAll),
            ("关闭 HTML 遮罩", HtmlMaskWindow.CloseAll)
        ],
        LogCleanupFailure);
        TaskRunnerFailurePolicy.ThrowCleanupFailures(cleanupFailures);
    }

    private void LogCleanupFailure(string step, Exception exception)
    {
        _logger.LogError(exception, "任务结束清理失败，步骤: {Step}", step);
    }

}

internal static class TaskRunnerCleanup
{
    internal static IReadOnlyList<Exception> RunAll(
        IEnumerable<(string Name, Action Action)> steps,
        Action<string, Exception> onFailure)
    {
        var failures = new List<Exception>();
        foreach (var (name, action) in steps)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                try
                {
                    onFailure(name, exception);
                }
                catch
                {
                    // 清理诊断本身失败时仍需继续执行后续清理步骤。
                }
            }
        }

        return failures;
    }
}

internal static class TaskRunnerFailurePolicy
{
    internal static void ThrowIfStartupCancelled(
        CancellationToken cancellationToken,
        bool propagateExceptions,
        bool isContinuousRunGroup = false)
    {
        if (propagateExceptions || isContinuousRunGroup)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    internal static void ThrowIfTaskCancelled(
        CancellationToken cancellationToken,
        bool propagateExceptions,
        bool isContinuousRunGroup = false)
    {
        ThrowIfStartupCancelled(
            cancellationToken,
            propagateExceptions,
            isContinuousRunGroup);
    }

    internal static Exception? GetTerminationException(
        Exception exception,
        bool isContinuousRunGroup,
        bool propagateExceptions)
    {
        return isContinuousRunGroup || propagateExceptions ? exception : null;
    }

    internal static void ThrowIfLockUnavailable(bool propagateExceptions)
    {
        if (propagateExceptions)
        {
            throw new InvalidOperationException("任务启动失败：当前存在正在运行中的独立任务。");
        }
    }

    internal static void ThrowAfterCleanup(
        Exception? executionException,
        IReadOnlyList<Exception> cleanupFailures,
        bool propagateCleanupFailures)
    {
        if (executionException is NormalEndException or OperationCanceledException)
        {
            ExceptionDispatchInfo.Capture(executionException).Throw();
        }

        if (executionException is not null && propagateCleanupFailures && cleanupFailures.Count > 0)
        {
            throw new AggregateException(
                "任务执行和清理均失败。",
                new[] { executionException }.Concat(cleanupFailures));
        }

        if (executionException is not null)
        {
            ExceptionDispatchInfo.Capture(executionException).Throw();
        }

        if (propagateCleanupFailures)
        {
            ThrowCleanupFailures(cleanupFailures);
        }
    }

    internal static void ThrowCleanupFailures(IReadOnlyList<Exception> cleanupFailures)
    {
        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException("任务结束清理失败。", cleanupFailures);
        }
    }
}
