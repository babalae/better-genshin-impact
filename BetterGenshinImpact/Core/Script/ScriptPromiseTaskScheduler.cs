using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Script;

/// <summary>
/// 承接 ClearScript 的 Task/Promise 完成回调，观察中断造成的回调异常并通知脚本入口，不泄漏到全局异常处理。
/// </summary>
internal sealed class ScriptPromiseTaskScheduler : TaskScheduler
{
    /// <summary>保护已排队和正在执行的任务集合。</summary>
    private readonly object _gate = new();

    /// <summary>尚未退出的脚本回调任务，用于引擎释放前的收尾等待。</summary>
    private readonly HashSet<Task> _tasks = [];

    /// <summary>第一条无法送回 JS Promise 的桥接异常，使用结果而非故障任务防止再次未观察。</summary>
    private readonly TaskCompletionSource<Exception> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>当前批次调度任务清空时完成，新增任务时重新创建。</summary>
    private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>当前脚本的 Promise 桥接错误信号。</summary>
    public Task<Exception> Failure => _failure.Task;

    /// <summary>创建初始空闲的脚本回调调度器。</summary>
    public ScriptPromiseTaskScheduler() => _idle.SetResult();

    /// <summary>把 V8 入口或 Promise 回调交给线程池，不占用 WPF UI 线程。</summary>
    protected override void QueueTask(Task task)
    {
        lock (_gate)
        {
            if (_tasks.Count == 0)
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _tasks.Add(task);
        }
        ThreadPool.UnsafeQueueUserWorkItem(static work => work.Scheduler.Execute(work.Task),
            (Scheduler: this, Task: task), preferLocal: false);
    }

    /// <summary>所有任务经过统一入口，避免内联回调绕过异常观察。</summary>
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

    /// <summary>提供调试器可读取的已调度任务快照。</summary>
    protected override IEnumerable<Task> GetScheduledTasks()
    {
        lock (_gate)
            return _tasks.ToArray();
    }

    /// <summary>执行并观察回调结果；V8 中断后 Promise 不再完成时，运行入口仍可收到故障或取消。</summary>
    private void Execute(Task task)
    {
        try
        {
            TryExecuteTask(task);
            if (task.IsFaulted)
            {
                // 读取 Exception 即观察 ClearScript 内部未返回给调用方的 continuation task。
                _failure.TrySetResult(task.Exception!);
            }
        }
        finally
        {
            lock (_gate)
            {
                _tasks.Remove(task);
                if (_tasks.Count == 0)
                    _idle.TrySetResult();
            }
        }
    }

    /// <summary>等宿主清理结束后，再等待当前已经投递的 V8 回调退出。</summary>
    public Task WaitForIdleAsync()
    {
        lock (_gate)
            return _idle.Task;
    }
}
