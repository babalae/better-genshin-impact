using System;
using BetterGenshinImpact.GameTask.AutoPathing.Movement;
using OpenCvSharp;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathing.Movement;

public class InertialTrackerTests
{
    [Fact]
    public void TrackLost_UsesSmoothedVelocityFromLastValidPosition()
    {
        var tracker = new InertialTracker();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        tracker.Reset(new Point2f(0, 0), start);
        tracker.MarkValid(new Point2f(10, 0), start.AddSeconds(1));

        var predicted = tracker.TrackLost(start.AddSeconds(3));

        Assert.InRange(predicted.X, 19.999f, 20.001f);
        Assert.InRange(predicted.Y, -0.001f, 0.001f);
        Assert.Equal(1, tracker.DistanceTooFarRetryCount);
    }

    [Fact]
    public void MarkValid_WhenVelocityExceedsLimit_DoesNotUseJumpForPrediction()
    {
        var tracker = new InertialTracker();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        tracker.Reset(new Point2f(0, 0), start);
        tracker.MarkValid(new Point2f(100, 0), start.AddSeconds(1));

        var predicted = tracker.TrackLost(start.AddSeconds(2));

        Assert.InRange(predicted.X, 99.999f, 100.001f);
        Assert.InRange(predicted.Y, -0.001f, 0.001f);
    }
}
