using Fischless.GameCapture.Graphics;

namespace BetterGenshinImpact.UnitTest.CoreTests.CaptureTests;

public class FrameCallbackLifetimeTests
{
    [Fact]
    public async Task BeginStopAndWait_ShouldRejectNewCallbacksAndDrainActiveCallback()
    {
        var lifetime = new FrameCallbackLifetime();
        Assert.True(lifetime.TryEnter());

        var stopTask = Task.Run(lifetime.BeginStopAndWait);
        Assert.True(SpinWait.SpinUntil(() => lifetime.IsStopping, TimeSpan.FromSeconds(1)));

        Assert.False(lifetime.TryEnter());
        Assert.False(stopTask.IsCompleted);

        lifetime.Exit();
        await stopTask.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Reset_ShouldAllowCallbacksAfterPreviousCaptureStopped()
    {
        var lifetime = new FrameCallbackLifetime();
        lifetime.BeginStopAndWait();

        Assert.False(lifetime.TryEnter());

        lifetime.Reset();
        Assert.True(lifetime.TryEnter());
        lifetime.Exit();
    }
}
