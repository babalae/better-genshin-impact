using BetterGenshinImpact.GameTask.AutoPathing.Suspend;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;

namespace SereniteaPot.Regression;

public class PotActiveTimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LongPause_DoesNotExhaustUiOrAreaDeadline(bool area)
    {
        var wall = new Clock();
        var components = new Dictionary<string, ISuspendable>();
        var paused = true;
        var stopCount = 2;
        var samples = 0;
        using (var active = new SereniteaPotActiveTime(components, wall))
        {
            async Task Delay(int ms, CancellationToken ct)
            {
                await SereniteaPotPauseWaiter.WaitAsync(() => paused, () => stopCount++, () => stopCount--,
                    () => { }, components.Values.ToArray(), (_, _) =>
                    {
                        Assert.True(active.IsSuspended);
                        wall.Advance(61000);
                        paused = false;
                        return Task.CompletedTask;
                    }, ct, e => throw e);
                await wall.Delay(ms, ct);
            }
            bool Observe() { samples++; return true; }
            var ready = area
                ? await MapAreaSwitchWaiter.WaitAsync(Observe, Delay, CancellationToken.None, timeProvider: active)
                : await SereniteaPotWaiter.WaitAsync(Observe, Delay, CancellationToken.None,
                    TimeSpan.FromSeconds(30), timeProvider: active);
            Assert.True(ready);
            Assert.Equal(area ? 1 : 3, samples);
            Assert.Equal(area ? 500 : 1500, active.Elapsed.TotalMilliseconds);
            Assert.Equal(61000 + active.Elapsed.TotalMilliseconds, wall.ElapsedMilliseconds);
            Assert.Equal(2, stopCount);
            Assert.False(active.IsSuspended);
        }
        Assert.Empty(components);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(120)]
    public void MovementDeadline_ExcludesPauseButStillExpiresForActiveTime(int seconds)
    {
        var wall = new Clock();
        var components = new Dictionary<string, ISuspendable>();
        using var active = new SereniteaPotActiveTime(components, wall);
        wall.Advance(1000);
        active.Suspend();
        active.Suspend();
        wall.Advance(180000);
        Assert.Equal(TimeSpan.FromSeconds(1), active.Elapsed);
        active.Resume();
        active.Resume();
        Assert.True(active.Elapsed < TimeSpan.FromSeconds(seconds));
        wall.Advance(seconds * 1000);
        Assert.True(active.Elapsed > TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public async Task SlowRecognition_StillConsumesDeadline()
    {
        var wall = new Clock();
        var components = new Dictionary<string, ISuspendable>();
        using var active = new SereniteaPotActiveTime(components, wall);
        var samples = 0;
        Assert.False(await SereniteaPotWaiter.WaitAsync(() =>
        {
            if (++samples == 3) wall.Advance(30000);
            return true;
        }, wall.Delay, CancellationToken.None, TimeSpan.FromSeconds(30), timeProvider: active));
        Assert.Equal(3, samples);
    }

    [Fact]
    public async Task CancellationDuringPause_ReleasesClockRegistrationAndOwnedCount()
    {
        using var cts = new CancellationTokenSource();
        var wall = new Clock();
        var components = new Dictionary<string, ISuspendable>();
        var count = 2;
        using (var active = new SereniteaPotActiveTime(components, wall))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SereniteaPotPauseWaiter.WaitAsync(
                () => true, () => count++, () => count--, () => { }, components.Values.ToArray(), (_, _) =>
                { wall.Advance(61000); cts.Cancel(); return Task.CompletedTask; }, cts.Token, e => throw e));
            Assert.Equal(2, count);
            Assert.False(active.IsSuspended);
            Assert.Equal(TimeSpan.Zero, active.Elapsed);
        }
        Assert.Empty(components);
    }

    [Fact]
    public void NestedClocks_DisposeOnlyTheirOwnRegistration()
    {
        var wall = new Clock();
        var components = new Dictionary<string, ISuspendable>();
        using var outer = new SereniteaPotActiveTime(components, wall);
        var inner = new SereniteaPotActiveTime(components, wall);
        Assert.Equal(2, components.Count);
        inner.Dispose();
        inner.Dispose();
        inner.Suspend();
        inner.Resume();
        Assert.Same(outer, Assert.Single(components).Value);
        outer.Suspend();
        wall.Advance(61000);
        outer.Resume();
        Assert.Equal(TimeSpan.Zero, outer.Elapsed);
    }

    [Fact]
    public void DisposedClock_DoesNotRemoveReplacementOrRecreateRegistration()
    {
        var components = new Dictionary<string, ISuspendable>();
        var first = new SereniteaPotActiveTime(components);
        var key = Assert.Single(components).Key;
        using var replacement = new SereniteaPotActiveTime(new Dictionary<string, ISuspendable>());
        components[key] = replacement;
        first.Dispose();
        first.Resume();
        Assert.Same(replacement, components[key]);
        Assert.Single(components);
    }

    private sealed class Clock : TimeProvider
    {
        private long milliseconds = 1000;
        internal long ElapsedMilliseconds => milliseconds - 1000;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => milliseconds;
        internal void Advance(int ms) => milliseconds += ms;
        internal Task Delay(int ms, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Advance(ms);
            return Task.CompletedTask;
        }
    }
}
