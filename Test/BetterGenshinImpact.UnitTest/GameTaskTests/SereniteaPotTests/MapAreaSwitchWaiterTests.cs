using BetterGenshinImpact.GameTask.AutoTrackPath;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.SereniteaPotTests;

public class MapAreaSwitchWaiterTests
{
    [Fact]
    public async Task MenuFrozenForTenSeconds_ShouldRecognizeItAfterItAppears()
    {
        var clock = new TestClock();
        Assert.True(await MapAreaSwitchWaiter.WaitAsync(
            () => clock.Milliseconds >= 10000, clock.Delay, CancellationToken.None, timeProvider: clock));
        Assert.Equal(10000, clock.Milliseconds);
    }

    [Fact]
    public async Task OcrOfOldFrameTakingTenSeconds_ShouldCaptureAgain()
    {
        var clock = new TestClock();
        var captures = 0;
        Assert.True(await MapAreaSwitchWaiter.WaitAsync(() =>
        {
            // 帧在菜单展开前取得，OCR 返回时菜单已经展开，必须重新取帧。
            if (++captures == 1)
            {
                clock.Advance(10000);
                return false;
            }
            return true;
        }, clock.Delay, CancellationToken.None, timeProvider: clock));
        Assert.Equal(2, captures);
        Assert.Equal(11000, clock.Milliseconds);
    }

    [Fact]
    public async Task MenuNeverAppears_ShouldFailAtDeadline()
    {
        var clock = new TestClock();
        Assert.False(await MapAreaSwitchWaiter.WaitAsync(
            () => false, clock.Delay, CancellationToken.None, timeProvider: clock));
        Assert.Equal(30000, clock.Milliseconds);
    }

    [Fact]
    public async Task TransientMissingCandidate_ShouldNotConfirmSelection()
    {
        var clock = new TestClock();
        var frames = new Queue<bool>([false, true, false, true, true]);
        Assert.True(await MapAreaSwitchWaiter.WaitAsync(
            () => frames.Dequeue(), clock.Delay, CancellationToken.None, stableChecks: 2, timeProvider: clock));
        Assert.Empty(frames);
    }

    [Fact]
    public async Task CancellationDuringOcr_ShouldNotReturnCandidateForClicking()
    {
        var clock = new TestClock();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MapAreaSwitchWaiter.WaitAsync(() =>
        {
            cancellation.Cancel();
            return true;
        }, clock.Delay, cancellation.Token, timeProvider: clock));
    }

    [Fact]
    public async Task CancellationDuringWait_ShouldNotTakeAnotherScreenshot()
    {
        var clock = new TestClock();
        using var cancellation = new CancellationTokenSource();
        var captures = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MapAreaSwitchWaiter.WaitAsync(
            () => { captures++; return true; },
            (ms, _) => { clock.Advance(ms); cancellation.Cancel(); return Task.CompletedTask; },
            cancellation.Token, timeProvider: clock));
        Assert.Equal(0, captures);
    }

    [Fact]
    public async Task OcrFinishingAfterDeadline_ShouldNotReturnStaleCandidateForClicking()
    {
        var clock = new TestClock();
        Assert.False(await MapAreaSwitchWaiter.WaitAsync(() =>
        {
            clock.Advance(30000);
            return true;
        }, clock.Delay, CancellationToken.None, timeProvider: clock));
    }

    private sealed class TestClock : TimeProvider
    {
        public long Milliseconds { get; private set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
        public void Advance(int ms) => Milliseconds += ms;
        public Task Delay(int ms, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Advance(ms);
            return Task.CompletedTask;
        }
    }
}
