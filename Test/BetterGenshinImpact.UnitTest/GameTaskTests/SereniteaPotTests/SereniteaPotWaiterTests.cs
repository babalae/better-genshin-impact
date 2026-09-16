using BetterGenshinImpact.GameTask.QuickSereniteaPot;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.SereniteaPotTests;

public class SereniteaPotWaiterTests
{
    [Fact]
    public async Task LoadingForFortySeconds_ShouldStillReachRewardPrecondition()
    {
        var clock = new TestClock();
        var ready = await Wait(clock, () => clock.Seconds >= 40);
        Assert.True(ready);
        Assert.InRange(clock.Seconds, 41, 42);
    }

    [Fact]
    public async Task MainUiWithoutPotMarker_ShouldTimeOut()
    {
        var clock = new TestClock();
        var mainUi = true;
        var potMarker = false;
        Assert.False(await Wait(clock, () => mainUi && potMarker));
        Assert.Equal(60, clock.Seconds);
    }

    [Fact]
    public async Task TransientMainUiDuringLoading_ShouldNotStartMovement()
    {
        var clock = new TestClock();
        Assert.True(await Wait(clock, () => clock.Seconds < 5 || clock.Seconds >= 30));
        Assert.InRange(clock.Seconds, 31, 32);
    }

    [Fact]
    public async Task FlickeringDetection_ShouldRequireConsecutiveFrames()
    {
        var clock = new TestClock();
        var samples = new Queue<bool>([true, true, false, true, false, true, true, true]);
        Assert.True(await SereniteaPotWaiter.WaitAsync(() => samples.Dequeue(), clock.Delay,
            CancellationToken.None, TimeSpan.FromSeconds(10), timeProvider: clock));
        Assert.Equal(4, clock.Seconds);
    }

    [Fact]
    public async Task SlowGadgetTabSelection_ShouldWaitForActualSelectedFrames()
    {
        var clock = new TestClock();
        var tabClickedAt = 0.5;
        var tabSelectedAt = 4.0;
        Assert.True(await SereniteaPotWaiter.WaitAsync(
            () => clock.Seconds >= tabClickedAt && clock.Seconds >= tabSelectedAt,
            clock.Delay, CancellationToken.None, TimeSpan.FromSeconds(20), timeProvider: clock));
        Assert.Equal(5, clock.Seconds);
    }

    [Fact]
    public async Task CancellationDuringLoad_ShouldPropagateWithoutMoreRecognition()
    {
        var clock = new TestClock();
        using var cts = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SereniteaPotWaiter.WaitAsync(() => { calls++; return false; },
                (ms, _) => { clock.Advance(ms); cts.Cancel(); return Task.CompletedTask; },
                cts.Token, TimeSpan.FromSeconds(60), timeProvider: clock));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task RecognitionOverrunningDeadline_ShouldNotReportSuccess()
    {
        var clock = new TestClock();
        var calls = 0;
        Assert.False(await SereniteaPotWaiter.WaitAsync(() =>
        {
            if (++calls == 3) clock.Advance(60000);
            return true;
        }, clock.Delay, CancellationToken.None, TimeSpan.FromSeconds(60), timeProvider: clock));
    }

    private static Task<bool> Wait(TestClock clock, Func<bool> predicate) =>
        SereniteaPotWaiter.WaitAsync(predicate, clock.Delay, CancellationToken.None,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), clock);

    private sealed class TestClock : TimeProvider
    {
        private long milliseconds;
        public double Seconds => milliseconds / 1000.0;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => milliseconds;
        public void Advance(int ms) => milliseconds += ms;
        public Task Delay(int ms, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Advance(ms);
            return Task.CompletedTask;
        }
    }
}
