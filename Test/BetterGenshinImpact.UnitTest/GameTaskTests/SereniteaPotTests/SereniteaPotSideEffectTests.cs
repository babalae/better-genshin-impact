using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.SereniteaPotTests;

public class SereniteaPotSideEffectTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PauseExit_RestoresOnlyOwnedState(bool cancel)
    {
        using var cts = new CancellationTokenSource();
        var count = 2; // 其他调用方仍然持有两次暂停。
        var paused = true;
        var active = new Component();
        var alreadyPaused = new Component { IsSuspended = true };
        var components = new List<ISuspendable> { active, alreadyPaused };
        var operation = SereniteaPotPauseWaiter.WaitAsync(() => paused, () => count++, () => count--,
            () => { }, components.ToArray(), (_, _) =>
            {
                components.Clear(); // 清理仍须恢复进入时持有的对象。
                if (cancel) cts.Cancel();
                else paused = false;
                return Task.CompletedTask;
            }, cts.Token, _ => { });
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        else await operation;
        Assert.Equal(2, count);
        Assert.False(active.IsSuspended);
        Assert.True(alreadyPaused.IsSuspended);
        Assert.Equal(0, alreadyPaused.ResumeCount);
    }

    [Fact]
    public async Task CancellationBeforePause_DoesNotAcquireState()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SereniteaPotPauseWaiter.WaitAsync(
            () => true, () => Assert.Fail("Must not acquire"), () => Assert.Fail("Must not release"),
            () => { }, [], Task.Delay, cts.Token, _ => { }));
    }

    [Fact]
    public async Task SuspendOrResumeFailure_DoesNotSkipRemainingCleanup()
    {
        var count = 0;
        var first = new Component { ThrowOnResume = true };
        var second = new Component { ThrowOnSuspend = true };
        var errors = new List<Exception>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SereniteaPotPauseWaiter.WaitAsync(
            () => true, () => count++, () => count--, () => { }, [first, second], Task.Delay,
            CancellationToken.None, errors.Add));
        Assert.Equal(0, count);
        Assert.False(second.IsSuspended);
        Assert.Single(errors);
        Assert.Equal(1, first.ResumeCount);
    }

    [Fact]
    public async Task CancelWhileHoldingPostMessageKey_ReleasesWithoutPressingAgain()
    {
        using var cts = new CancellationTokenSource();
        var down = false;
        var presses = 0;
        var hold = new SereniteaPotInputHold(() => { down = true; presses++; }, () => down = false, cts.Token);
        hold.Resume();
        Assert.True(down);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SereniteaPotPauseWaiter.WaitAsync(
            () => true, () => { }, () => { }, () => Assert.False(down), [hold], (_, _) =>
            { cts.Cancel(); return Task.CompletedTask; }, cts.Token, _ => { }));
        Assert.False(down);
        Assert.Equal(1, presses);
        hold.Dispose();
        hold.Resume();
        Assert.False(down);
    }

    [Fact]
    public void Hold_ResumesAfterPauseAndReleasesAfterException()
    {
        var down = false;
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var hold = new SereniteaPotInputHold(() => down = true, () => down = false, CancellationToken.None);
            hold.Resume();
            hold.Suspend();
            Assert.False(down);
            hold.Resume();
            Assert.True(down);
            throw new InvalidOperationException();
        }));
        Assert.False(down);
    }

    [Fact]
    public async Task VisiblePot_DoesNotScroll()
    {
        Assert.True(await SereniteaPotInventorySearch.FindAsync(() => true,
            (_, _) => throw new InvalidOperationException("Visible pot must use fast path"), CancellationToken.None));
    }

    [Fact]
    public async Task UnknownFrames_DoNotCountAsTopOrKeepEarlierStability()
    {
        var samples = new Queue<PotScrollObservation>([
            PotScrollObservation.Stable, PotScrollObservation.Stable, PotScrollObservation.Unknown,
            PotScrollObservation.Stable, PotScrollObservation.Moved,
            PotScrollObservation.Stable, PotScrollObservation.Stable, PotScrollObservation.Stable]);
        var upSteps = 0;
        var downSteps = 0;
        var found = await SereniteaPotInventorySearch.FindAsync(() => downSteps == 1, (direction, _) =>
        {
            if (direction > 0) { upSteps++; return Task.FromResult(samples.Dequeue()); }
            downSteps++;
            return Task.FromResult(PotScrollObservation.Moved);
        }, CancellationToken.None);
        Assert.True(found);
        Assert.Equal(8, upSteps);
        Assert.Equal(1, downSteps);
    }

    [Fact]
    public async Task InvalidFrames_ReachBoundWithoutClaimingTopOrScanningDown()
    {
        var steps = 0;
        Assert.False(await SereniteaPotInventorySearch.FindAsync(() => false, (direction, _) =>
        {
            Assert.Equal(1, direction);
            steps++;
            return Task.FromResult(PotScrollObservation.Unknown);
        }, CancellationToken.None, maxScrollSteps: 5));
        Assert.Equal(5, steps);
    }

    [Fact]
    public async Task CancellationDuringScroll_DoesNotClickAnotherFrame()
    {
        using var cts = new CancellationTokenSource();
        var scans = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SereniteaPotInventorySearch.FindAsync(
            () => { scans++; return false; }, (_, _) =>
            { cts.Cancel(); return Task.FromResult(PotScrollObservation.Stable); }, cts.Token));
        Assert.Equal(1, scans);
    }

    [Theory]
    [InlineData(false, 1, 0, 0, 0, "Unknown")] // 空白图
    [InlineData(true, 0.1, 0, 0, 0, "Unknown")] // 低相关度
    [InlineData(true, 1, 0, 0, 0, "Stable")]
    [InlineData(true, 0.98, 0, 20, 0, "Moved")] // 高相关也可能移动
    [InlineData(true, 0.7, 0, 0, 0, "Unknown")]
    [InlineData(true, 1, 0, 0, 10, "Unknown")] // 过渡/亮度变化
    [InlineData(true, double.NaN, 0, 0, 0, "Unknown")]
    public void ScrollClassification_RequiresPositiveEvidence(bool valid, double response,
        double x, double y, double difference, string expected) =>
        Assert.Equal(expected, SereniteaPotInventorySearch.Classify(valid, response, x, y, difference).ToString());

    private sealed class Component : ISuspendable
    {
        public bool IsSuspended { get; set; }
        public bool ThrowOnSuspend { get; init; }
        public bool ThrowOnResume { get; init; }
        public int ResumeCount;
        public void Suspend()
        {
            IsSuspended = true;
            if (ThrowOnSuspend) throw new InvalidOperationException("Suspend failed after changing state");
        }
        public void Resume()
        {
            ResumeCount++;
            if (ThrowOnResume) throw new InvalidOperationException("Resume failed");
            IsSuspended = false;
        }
    }
}
