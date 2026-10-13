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
