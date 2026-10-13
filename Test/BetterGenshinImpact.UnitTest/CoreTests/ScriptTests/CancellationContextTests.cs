using BetterGenshinImpact.Core.Script;

namespace BetterGenshinImpact.UnitTest.CoreTests.ScriptTests;

public class CancellationContextTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelDuringGroup_ShouldSurviveSubgroupClearAndSet(bool manual)
    {
        var context = new CancellationContext();
        context.Clear();
        using var scope = context.TryBeginScope();
        Assert.NotNull(scope);
        var source = context.Cts;

        Cancel(context, manual);
        context.Clear();
        context.Set();

        Assert.Same(source, context.Cts);
        Assert.Equal(scope.Token, context.Cts.Token);
        Assert.True(context.IsCancellationRequested);
        Assert.Equal(manual, context.IsManualStop);
        Assert.Throws<OperationCanceledException>(scope.Token.ThrowIfCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelBetweenGroups_ShouldInterruptWaitAndPreventNextGroup(bool manual)
    {
        var context = new CancellationContext();
        context.Clear();
        using var scope = context.TryBeginScope();
        Assert.NotNull(scope);

        context.Set();
        context.Clear();
        var wait = Task.Delay(Timeout.Infinite, scope.Token);
        Assert.False(wait.IsCompleted);

        Cancel(context, manual);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => wait.WaitAsync(TimeSpan.FromSeconds(1)));
        context.Set();
        Assert.Equal(manual, context.IsManualStop);
        Assert.Throws<OperationCanceledException>(scope.Token.ThrowIfCancellationRequested);
    }

    [Fact]
    public void TryBeginScope_WhenAlreadyRunning_ShouldPreserveActiveScope()
    {
        var context = new CancellationContext();
        context.Clear();
        using var scope = context.TryBeginScope();
        Assert.NotNull(scope);
        var source = context.Cts;
        context.ManualCancel();

        using var duplicate = context.TryBeginScope();

        Assert.Null(duplicate);
        Assert.Same(source, context.Cts);
        Assert.Equal(scope.Token, context.Cts.Token);
        Assert.True(scope.Token.IsCancellationRequested);
        Assert.True(context.IsManualStop);
    }

    [Fact]
    public void Dispose_ShouldAllowFreshScopeAndIgnorePreviousScopeDisposal()
    {
        var context = new CancellationContext();
        context.Clear();
        using var previousScope = context.TryBeginScope();
        Assert.NotNull(previousScope);
        var previousSource = context.Cts;
        context.ManualCancel();
        previousScope.Dispose();

        Assert.Throws<ObjectDisposedException>(() => previousSource.Token);
        using var scope = context.TryBeginScope();
        Assert.NotNull(scope);
        var source = context.Cts;
        Assert.NotEqual(previousScope.Token, scope.Token);
        Assert.False(scope.Token.IsCancellationRequested);
        Assert.False(context.IsManualStop);

        previousScope.Dispose();

        Assert.Same(source, context.Cts);
        Assert.Equal(scope.Token, source.Token);
        context.Cancel();
        Assert.True(scope.Token.IsCancellationRequested);
    }

    [Fact]
    public void SetAndClear_WithoutScope_ShouldRetainStandaloneLifecycle()
    {
        var context = new CancellationContext();
        context.Clear();
        context.Set();
        using var previousSource = context.Cts;
        context.ManualCancel();

        context.Set();
        using var source = context.Cts;

        Assert.NotSame(previousSource, source);
        Assert.False(context.IsCancellationRequested);
        Assert.False(context.IsManualStop);
        context.Clear();
        context.Clear();
        context.ManualCancel();
        context.Cancel();
        Assert.False(context.IsCancellationRequested);
        Assert.False(context.IsManualStop);
        Assert.Throws<ObjectDisposedException>(() => source.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentEntry_ShouldNotJoinOrResetActiveRun(bool continuousRun)
    {
        var context = new CancellationContext();
        context.Clear();
        var started = CreateSignal();
        CancellationTokenSource? source = null;
        var running = RunEntryAsync(async () =>
        {
            using var scope = continuousRun ? context.TryBeginScope() : context.TryJoinOrBeginScope();
            Assert.NotNull(scope);
            source = context.Cts;
            if (continuousRun)
            {
                // 子任务退出后的组间等待仍然属于连续执行。
                using var child = context.TryJoinOrBeginScope();
                Assert.NotNull(child);
            }
            started.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Task.Delay(Timeout.Infinite, scope.Token));
        });

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await RunEntryAsync(() =>
            {
                using var rejected = continuousRun ? context.TryJoinOrBeginScope() : context.TryBeginScope();
                Assert.Null(rejected);
                Assert.Throws<InvalidOperationException>(context.Set);
                Assert.Throws<InvalidOperationException>(context.Clear);
                Assert.Same(source, context.Cts);
                Assert.False(context.IsCancellationRequested);
                return Task.CompletedTask;
            });
        }
        finally
        {
            context.ManualCancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.True(context.IsManualStop);
        Assert.Throws<ObjectDisposedException>(() => source!.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TaskRunChild_ShouldJoinOwnerAndReceiveCancellation(bool manual)
    {
        var context = new CancellationContext();
        context.Clear();
        using var scope = context.TryBeginScope();
        Assert.NotNull(scope);
        var source = context.Cts;
        var started = CreateSignal();
        var childTask = Task.Run(async () =>
        {
            using var child = context.TryJoinOrBeginScope();
            Assert.NotNull(child);
            Assert.NotSame(scope, child);
            Assert.Equal(scope.Token, child.Token);
            context.Set();
            context.Clear();
            Assert.Same(source, context.Cts);
            started.SetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => Task.Delay(Timeout.Infinite, child.Token));
        });

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Cancel(context, manual);
            await childTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(manual, context.IsManualStop);
        Assert.Equal(scope.Token, source.Token);
        Assert.True(scope.Token.IsCancellationRequested);
    }

    [Fact]
    public void DisposeChild_ShouldRestoreParentWithoutDisposingCancellationSource()
    {
        var context = new CancellationContext();
        context.Clear();
        using var scope = context.TryBeginScope();
        Assert.NotNull(scope);
        var source = context.Cts;
        var child = context.TryJoinOrBeginScope();
        Assert.NotNull(child);

        child.Dispose();
        child.Dispose();
        context.Set();
        context.Clear();
        using var nextChild = context.TryJoinOrBeginScope();

        Assert.NotNull(nextChild);
        Assert.NotSame(child, nextChild);
        Assert.Same(source, context.Cts);
        Assert.Equal(scope.Token, source.Token);
        context.Cancel();
        Assert.True(nextChild.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task DisposeParentBeforeChild_ShouldKeepCancellationAndExclusivityUntilChildEnds()
    {
        var context = new CancellationContext();
        context.Clear();
        var childStarted = CreateSignal();
        var parentEnded = CreateSignal();
        CancellationTokenSource? source = null;
        var running = RunEntryAsync(async () =>
        {
            using var parent = context.TryBeginScope();
            Assert.NotNull(parent);
            source = context.Cts;
            var childTask = Task.Run(async () =>
            {
                using var child = context.TryJoinOrBeginScope();
                Assert.NotNull(child);
                childStarted.SetResult();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => Task.Delay(Timeout.Infinite, child.Token));
            });
            await childStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            parent.Dispose();
            parentEnded.SetResult();
            await childTask.WaitAsync(TimeSpan.FromSeconds(5));
        });

        try
        {
            await parentEnded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await RunEntryAsync(() =>
            {
                using var rejectedRoot = context.TryBeginScope();
                using var rejectedChild = context.TryJoinOrBeginScope();
                Assert.Null(rejectedRoot);
                Assert.Null(rejectedChild);
                Assert.Equal(source!.Token, context.Cts.Token);
                Assert.False(context.IsCancellationRequested);
                return Task.CompletedTask;
            });
        }
        finally
        {
            context.ManualCancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Throws<ObjectDisposedException>(() => source!.Token);
        using var fresh = context.TryBeginScope();
        Assert.NotNull(fresh);
        Assert.False(fresh.Token.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CapturedDisposedEntry_ShouldNotStartOrJoinLaterRun(bool hasNewRun)
    {
        var context = new CancellationContext();
        context.Clear();
        var captured = CreateSignal();
        var resume = CreateSignal();
        Task? delayedChild = null;
        await RunEntryAsync(async () =>
        {
            using var oldScope = context.TryBeginScope();
            Assert.NotNull(oldScope);
            delayedChild = Task.Run(async () =>
            {
                captured.SetResult();
                await resume.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using var joined = context.TryJoinOrBeginScope();
                using var started = context.TryBeginScope();
                Assert.Null(joined);
                Assert.Null(started);
            });
            await captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        });

        using var newScope = hasNewRun ? context.TryBeginScope() : null;
        var source = context.Cts;
        if (hasNewRun)
        {
            Assert.NotNull(newScope);
        }
        resume.SetResult();
        await delayedChild!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(source, context.Cts);
        if (hasNewRun)
        {
            Assert.Equal(newScope!.Token, source.Token);
            Assert.False(context.IsCancellationRequested);
        }
        else
        {
            Assert.Throws<ObjectDisposedException>(() => source.Token);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentIndependentEntries_ShouldAllowExactlyOneOwner(bool allowJoin)
    {
        const int entryCount = 8;
        var context = new CancellationContext();
        context.Clear();
        var start = CreateSignal();
        var attempted = CreateSignal();
        var release = CreateSignal();
        var attempts = 0;
        var owners = 0;
        var entries = Enumerable.Range(0, entryCount).Select(_ => RunEntryAsync(async () =>
        {
            await start.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var scope = allowJoin ? context.TryJoinOrBeginScope() : context.TryBeginScope();
            if (scope != null)
            {
                Interlocked.Increment(ref owners);
            }
            if (Interlocked.Increment(ref attempts) == entryCount)
            {
                attempted.SetResult();
            }
            // 胜者必须等所有入口尝试完毕，避免顺序执行也被误认为竞争成功。
            await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
        })).ToArray();

        try
        {
            start.SetResult();
            await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, owners);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(entries).WaitAsync(TimeSpan.FromSeconds(5));
        }

        using var fresh = context.TryBeginScope();
        Assert.NotNull(fresh);
    }

    private static TaskCompletionSource CreateSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task RunEntryAsync(Func<Task> action)
    {
        // 从调用者的干净上下文进入独立异步方法，避免把某次入口的 AsyncLocal 泄漏到其他入口。
        await Task.Yield();
        await action();
    }

    private static void Cancel(CancellationContext context, bool manual)
    {
        if (manual)
        {
            context.ManualCancel();
        }
        else
        {
            context.Cancel();
        }
    }
}
