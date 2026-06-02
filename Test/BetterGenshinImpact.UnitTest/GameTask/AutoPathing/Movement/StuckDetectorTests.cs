using System;
using BetterGenshinImpact.GameTask.AutoPathing.Movement;
using OpenCvSharp;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathing.Movement;

public class StuckDetectorTests
{
    [Fact]
    public void CheckStuck_WhenPositionDoesNotMoveAcrossWindow_ReturnsTrue()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var detector = new StuckDetector(() => now);
        var position = new Point2f(10, 20);

        for (var i = 0; i < 8; i++)
        {
            now = now.AddMilliseconds(1001);
            Assert.False(detector.CheckStuck(position, 0));
        }

        now = now.AddMilliseconds(1001);
        Assert.True(detector.CheckStuck(position, 0));
        Assert.Equal(1, detector.InTrapCount);
    }

    [Fact]
    public void Reset_ClearsTrapCountAndRestartsSamplingTime()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var detector = new StuckDetector(() => now);
        var position = new Point2f(10, 20);

        for (var i = 0; i < 9; i++)
        {
            now = now.AddMilliseconds(1001);
            detector.CheckStuck(position, 0);
        }

        Assert.Equal(1, detector.InTrapCount);

        detector.Reset();
        Assert.Equal(0, detector.InTrapCount);

        now = now.AddMilliseconds(1000);
        Assert.False(detector.CheckStuck(position, 0));
    }
}
