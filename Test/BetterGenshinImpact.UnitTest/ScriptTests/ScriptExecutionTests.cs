using BetterGenshinImpact.Core.Script;
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;

namespace BetterGenshinImpact.UnitTest.ScriptTests;

/// <summary>
/// 使用真实 V8 与受控宿主验证 JS 取消，不初始化应用、不启动游戏或模拟输入。
/// </summary>
[Collection("ScriptExecution")]
public sealed class ScriptExecutionTests
{
    /// <summary>不再 settle 的纯 JS Promise 也必须具有独立的取消出口。</summary>
    [Fact]
    public async Task NeverSettledPromise_CanBeStopped()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var execution = ScriptExecution.ExecuteAsync(_ =>
        {
            var engine = CreateEngine();
            engine.AddHostObject("entered", new Action(() => entered.TrySetResult()));
            return engine;
        }, engine => engine.Evaluate("new Promise(() => entered())"), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    /// <summary>同步死循环仍由 V8 中断，不能只取消 Promise 等待而保留 JS 执行线程。</summary>
    [Fact]
    public async Task SynchronousLoop_CanBeStopped()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var execution = ScriptExecution.ExecuteAsync(_ =>
        {
            var engine = CreateEngine();
            engine.AddHostObject("entered", new Action(() => entered.TrySetResult()));
            return engine;
        }, engine => engine.Evaluate("entered(); while (true) {}"), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    /// <summary>宿主取消后 JS catch 或 Promise 桥接中断均不能阻止运行收尾，宿主 finally 必须先退出。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostCancellation_WaitsForCleanupEvenIfScriptCatches(bool catchesCancellation)
    {
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var cleanupEntered = Signal();
        var allowCleanup = Signal();
        var cleaned = false;
        var execution = ScriptExecution.ExecuteAsync(token =>
        {
            var engine = CreateEngine();
            engine.AddHostObject("route", new Func<Task>(async () =>
            {
                using var operation = ScriptHostOperations.Enter();
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                }
                finally
                {
                    cleanupEntered.TrySetResult();
                    await allowCleanup.Task.ConfigureAwait(false);
                    cleaned = true;
                }
            }));
            return engine;
        }, engine => engine.Evaluate(catchesCancellation
            ? "(async () => { try { await route(); } catch (error) {} await new Promise(() => {}); })()"
            : "(async () => { await route(); })()"), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancellation.Cancel();
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(execution.IsCompleted);
            allowCleanup.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.True(cleaned);
        }
        finally
        {
            allowCleanup.TrySetResult();
            cancellation.Cancel();
        }
    }

    /// <summary>即使脚本未 await 已启动的路线，提前返回也不能绕过宿主清理。</summary>
    [Fact]
    public async Task UnawaitedHostOperation_IsCancelledAndDrainedBeforeReturn()
    {
        var cleanupEntered = Signal();
        var allowCleanup = Signal();
        var cleaned = false;
        var execution = ScriptExecution.ExecuteAsync(token =>
        {
            var engine = CreateEngine();
            engine.AddHostObject("route", new Func<Task>(async () =>
            {
                using var operation = ScriptHostOperations.Enter();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                finally
                {
                    cleanupEntered.TrySetResult();
                    await allowCleanup.Task;
                    cleaned = true;
                }
            }));
            return engine;
        }, engine => engine.Evaluate("route(); 1;"), CancellationToken.None);
        try
        {
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(execution.IsCompleted);
            allowCleanup.TrySetResult();
            await execution.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(cleaned);
        }
        finally
        {
            allowCleanup.TrySetResult();
        }
    }

    /// <summary>正常宿主 Task 转 Promise 仍可 await，脚本错误不被当成用户停止压制。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AwaitedHostResult_PreservesScriptErrors(bool throwsScriptError)
    {
        var execution = ScriptExecution.ExecuteAsync(_ =>
        {
            var engine = CreateEngine();
            engine.AddHostObject("value", new Func<Task<int>>(() => Task.FromResult(42)));
            return engine;
        }, engine => engine.Evaluate(throwsScriptError
            ? "(async () => { await value(); throw new Error('脚本业务失败'); })()"
            : "(async () => { if (await value() !== 42) throw new Error('结果错误'); })()"), CancellationToken.None);
        if (throwsScriptError)
        {
            var exception = await Assert.ThrowsAnyAsync<ScriptEngineException>(() => execution.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Contains("脚本业务失败", exception.Message);
        }
        else
            await execution.WaitAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>模块顶层 await 同样使用本轮调度器和独立取消出口。</summary>
    [Fact]
    public async Task ModuleTopLevelAwait_CanBeStopped()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var execution = ScriptExecution.ExecuteAsync(token =>
        {
            var engine = CreateEngine();
            engine.AddHostObject("route", new Func<Task>(async () =>
            {
                using var operation = ScriptHostOperations.Enter();
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }));
            return engine;
        }, engine => engine.Evaluate(new DocumentInfo("stop-test-module") { Category = ModuleCategory.Standard },
            "export const started = true; await route();"), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    /// <summary>桥接回调无法访问引擎时必须通知调用方失败，不能永远等待不再完成的 Promise。</summary>
    [Fact]
    public async Task BrokenPromiseCallback_ReportsFailureInsteadOfHanging()
    {
        var entered = Signal();
        var evaluated = Signal();
        var release = Signal();
        V8ScriptEngine? currentEngine = null;
        var execution = ScriptExecution.ExecuteAsync(_ =>
        {
            currentEngine = CreateEngine();
            currentEngine.AddHostObject("route", new Func<Task>(async () =>
            {
                using var operation = ScriptHostOperations.Enter();
                entered.TrySetResult();
                await release.Task;
            }));
            return currentEngine;
        }, engine =>
        {
            var promise = engine.Evaluate("(async () => { await route(); })()");
            evaluated.TrySetResult();
            return promise;
        }, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await evaluated.Task.WaitAsync(TimeSpan.FromSeconds(15));
            // 使用仅本测试持有的引擎制造确定的桥接故障；空闲引擎上的 Interrupt 不保证下一回调失败。
            currentEngine!.Dispose();
            release.TrySetResult();
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => execution.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.IsNotType<TimeoutException>(exception);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    /// <summary>一个脚本的快捷键取消不能影响另一个脚本的宿主作用域和引擎。</summary>
    [Fact]
    public async Task ConcurrentExecutions_CancellationIsIsolated()
    {
        using var firstCancellation = new CancellationTokenSource();
        var firstEntered = Signal();
        var secondEntered = Signal();
        var releaseSecond = Signal();
        var first = ScriptExecution.ExecuteAsync(token =>
        {
            var engine = CreateEngine();
            engine.AddHostObject("route", new Func<Task>(async () =>
            {
                using var operation = ScriptHostOperations.Enter();
                firstEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }));
            return engine;
        }, engine => engine.Evaluate("(async () => { await route(); })()"), firstCancellation.Token);
        var second = ScriptExecution.ExecuteAsync(token =>
        {
            var engine = CreateEngine();
            engine.AddHostObject("route", new Func<Task>(async () =>
            {
                using var operation = ScriptHostOperations.Enter();
                secondEntered.TrySetResult();
                await releaseSecond.Task;
                token.ThrowIfCancellationRequested();
            }));
            return engine;
        }, engine => engine.Evaluate("(async () => { await route(); })()"), CancellationToken.None);
        try
        {
            await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(TimeSpan.FromSeconds(15));
            firstCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.False(second.IsCompleted);
            releaseSecond.TrySetResult();
            await second.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            firstCancellation.Cancel();
            releaseSecond.TrySetResult();
        }
    }

    /// <summary>中断产生的 Promise 回调异常必须在本轮观察，不能在 GC 后触发应用的全局异常事件。</summary>
    [Fact]
    public async Task CancelledPromiseCallbacks_DoNotRaiseUnobservedScriptInterruptions()
    {
        var unobserved = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            foreach (var exception in args.Exception.Flatten().InnerExceptions)
                if (exception is ScriptInterruptedException)
                    unobserved.Enqueue(exception);
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            for (var i = 0; i < 8; i++)
                await ExecuteCancelledHostPromiseAsync();
            // 回调任务已退出；强制收集仅测试进程的短命对象，验证没有被全局过滤器掩盖的中断异常。
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    /// <summary>创建并完整结束一个会触发 Task/Promise 取消桥接的短命引擎，不把其对象保留到 GC 断言。</summary>
    private static async Task ExecuteCancelledHostPromiseAsync()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = Signal();
        var execution = ScriptExecution.ExecuteAsync(token =>
        {
            var engine = CreateEngine();
            engine.AddHostObject("route", new Func<Task>(async () =>
            {
                using var operation = ScriptHostOperations.Enter();
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }));
            return engine;
        }, engine => engine.Evaluate("(async () => { await route(); })()"), cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    /// <summary>每个测试只创建兼容生产标志的 V8，不添加真实 BetterGI 宿主对象。</summary>
    private static V8ScriptEngine CreateEngine() => new(V8ScriptEngineFlags.UseCaseInsensitiveMemberBinding
        | V8ScriptEngineFlags.EnableTaskPromiseConversion);

    /// <summary>建立可控、不会同步运行测试后续步骤的信号。</summary>
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>全局异常观察测试不与其他测试并行，防止把其他测试的对象收集归因于本轮脚本。</summary>
[CollectionDefinition("ScriptExecution", DisableParallelization = true)]
public sealed class ScriptExecutionCollection
{
}
