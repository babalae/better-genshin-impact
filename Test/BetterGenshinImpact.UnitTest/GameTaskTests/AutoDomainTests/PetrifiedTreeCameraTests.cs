using BetterGenshinImpact.GameTask.AutoDomain;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomainTests;

public class PetrifiedTreeCameraTests
{
    [Theory]
    [InlineData(960, 1920, 0)]
    [InlineData(865, 1920, 0)]
    [InlineData(1055, 1920, 0)]
    [InlineData(496, 1920, -44)]
    [InlineData(1424, 1920, 44)]
    [InlineData(1280, 2560, 0)]
    [InlineData(816, 2560, -33)]
    [InlineData(1920, 3840, 0)]
    [InlineData(1456, 3840, -22)]
    [InlineData(-1000, 1920, -120)]
    [InlineData(3000, 1920, 120)]
    public void CalculatePetrifiedTreeMouseDelta_UsesToleranceAndClamp(
        int treeMiddleX,
        int captureWidth,
        int expected)
    {
        var result = AutoDomainTask.CalculatePetrifiedTreeMouseDelta(treeMiddleX, captureWidth);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculatePetrifiedTreeMouseDelta_RejectsInvalidCaptureWidth()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutoDomainTask.CalculatePetrifiedTreeMouseDelta(0, 0));
    }

    [Fact]
    public void ArePetrifiedTreeDetectionsConsistent_AcceptsNearbyFrames()
    {
        var first = new Rect(800, 300, 300, 500);
        var second = new Rect(820, 310, 300, 500);

        Assert.True(AutoDomainTask.ArePetrifiedTreeDetectionsConsistent(
            first, second, 1920, 1080));
    }

    [Fact]
    public void ArePetrifiedTreeDetectionsConsistent_RejectsUnrelatedOrInvalidFrames()
    {
        var first = new Rect(800, 300, 300, 500);

        Assert.False(AutoDomainTask.ArePetrifiedTreeDetectionsConsistent(
            first, new Rect(1400, 300, 300, 500), 1920, 1080));
        Assert.False(AutoDomainTask.ArePetrifiedTreeDetectionsConsistent(
            first, new Rect(1800, 300, 300, 500), 1920, 1080));
        Assert.False(AutoDomainTask.ArePetrifiedTreeDetectionsConsistent(
            first, new Rect(820, 310, 0, 500), 1920, 1080));
    }
}
