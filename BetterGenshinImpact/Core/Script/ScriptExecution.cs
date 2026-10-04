using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ClearScript.V8;

namespace BetterGenshinImpact.Core.Script;

/// <summary>
/// 独立的 JS 生命周期入口：取消等待不依赖 Promise 拒绝回调，同时在退出前等待已启动的宿主操作清理。
/// </summary>
public static class ScriptExecution
{
    /// <summary>
    /// 创建本轮引擎并求值，兼容普通脚本和模块；引擎、取消登记、Promise 桥接和宿主操作均在本轮收尾。
    /// </summary>
    public static async Task ExecuteAsync(Func<CancellationToken, V8ScriptEngine> createEngine,
        Func<V8ScriptEngine, object?> evaluate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var operations = new ScriptHostOperations(ct);
        var scheduler = new ScriptPromiseTaskScheduler();
        V8ScriptEngine? engine = null;
        CancellationTokenRegistration registration = default;
        try
        {
            // ClearScript 7.4.5 在 Task 转 Promise 时使用 TaskScheduler.Current。
            // 固定到本轮调度器，既不修改全局异常过滤，也不让中断后的回调无人观察。
            var evaluation = await Task.Factory.StartNew(() =>
            {
                using var context = operations.Activate();
                ct.ThrowIfCancellationRequested();
                engine = createEngine(operations.Token);
                registration = ct.Register(() => TryInterrupt(engine));
                ct.ThrowIfCancellationRequested();
                return evaluate(engine);
            }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, scheduler).ConfigureAwait(false);

            if (evaluation is Task promise)
            {
                Observe(promise);
                // 中断可能使 Promise 永不 settle；取消必须有独立出口，不能仅 await promise。
                var completed = await Task.WhenAny(promise, scheduler.Failure).WaitAsync(ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (completed == scheduler.Failure)
                    ExceptionDispatchInfo.Capture(await scheduler.Failure.ConfigureAwait(false)).Throw();
                await promise.ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException("JS 脚本已停止。", ex, ct);
        }
        finally
        {
            try
            {
                try
                {
                    // 终止代码执行；这里只打断 JS，宿主业务必须实际退出才能归还游戏输入所有权。
                    TryInterrupt(engine);
                }
                finally
                {
                    // 中断自身失败也不能跳过已启动宿主的清理，避免旧任务仍在操作游戏时放行新任务。
                    await operations.StopAndDrainAsync().ConfigureAwait(false);
                    await scheduler.WaitForIdleAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                // 先注销中断回调再释放引擎，避免取消与 Dispose 并发访问同一引擎。
                registration.Dispose();
                engine?.Dispose();
            }
        }
    }

    /// <summary>观察取消出口不再等待的 Promise 后续故障，避免它稍后转入全局异常事件。</summary>
    private static void Observe(Task promise)
    {
        _ = promise.ContinueWith(completed =>
        {
            if (completed.IsFaulted)
                _ = completed.Exception;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// 尽力中断 V8 执行；引擎已经结束或释放时无需覆盖原始执行结果。
    /// 只忽略引擎已经释放的中断竞争，不压制正常脚本执行错误。
    /// </summary>
    private static void TryInterrupt(V8ScriptEngine? engine)
    {
        try
        {
            engine?.Interrupt();
        }
        catch (ObjectDisposedException)
        {
            // 晚到的停止信号不应覆盖已经完成的脚本结果。
        }
    }
}
