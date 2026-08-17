using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.CommonJobTests;

public class ScanPickTaskTests
{
    [Fact]
    public void SortPickItems_ShouldPreferGroundedCenterBeam()
    {
        var farRightBeam = new Rect(1557, 608, 16, 109);
        var centerBeam = new Rect(1124, 632, 43, 164);
        var upperParticle = new Rect(1016, 502, 25, 60);

        var result = ScanPickTask.SortPickItems([farRightBeam, centerBeam, upperParticle], 1920, 1080).ToList();

        Assert.Equal(centerBeam, result[0]);
    }

    [Fact]
    public void GetMovementDecision_ShouldScaleFrom1080PThresholds()
    {
        var decision1080P = ScanPickTask.GetMovementDecision(new Rect(1124, 632, 43, 164), 1920, 1080);
        var decision900P = ScanPickTask.GetMovementDecision(new Rect(936, 527, 36, 137), 1600, 900);

        Assert.Equal(decision1080P, decision900P);
        Assert.False(decision1080P.Pickup);
        Assert.False(decision1080P.Left);
        Assert.True(decision1080P.Right);
        Assert.False(decision1080P.Forward);
        Assert.False(decision1080P.Backward);
    }

    [Fact]
    public void GetMovementDecision_ShouldPickCenteredGroundDropInsteadOfWalkingPastIt()
    {
        var decision = ScanPickTask.GetMovementDecision(new Rect(934, 685, 38, 238), 1920, 1080);

        Assert.True(decision.Pickup);
        Assert.False(decision.Left);
        Assert.False(decision.Right);
        Assert.False(decision.Forward);
        Assert.False(decision.Backward);
    }

    [Fact]
    public void GetMovementDecision_ShouldSteerTowardsDistantDiagonalDrop()
    {
        var decision = ScanPickTask.GetMovementDecision(new Rect(1400, 300, 40, 100), 1920, 1080);

        Assert.False(decision.Pickup);
        Assert.False(decision.Left);
        Assert.True(decision.Right);
        Assert.True(decision.Forward);
        Assert.False(decision.Backward);
    }
}
