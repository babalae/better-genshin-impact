using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.CommonJobTests;

public class ScanPickTaskTests
{
    [Fact]
    public void DetectGreenLootPillars_ShouldFindVerticalGreenBeam()
    {
        using var mat = CreateEmptyFrame();
        Cv2.Rectangle(mat, new Rect(1140, 650, 20, 140), new Scalar(80, 255, 80), -1);

        var result = ScanPickTask.DetectGreenLootPillars(mat);

        Assert.Contains(result, rect =>
            rect.X <= 1140 &&
            rect.Right >= 1160 &&
            rect.Y <= 650 &&
            rect.Bottom >= 790);
    }

    [Fact]
    public void DetectGreenLootPillars_ShouldIgnoreLowerLeftRewardOverlay()
    {
        using var mat = CreateEmptyFrame();
        Cv2.Rectangle(mat, new Rect(150, 700, 60, 90), new Scalar(80, 255, 80), -1);

        var result = ScanPickTask.DetectGreenLootPillars(mat);

        Assert.Empty(result);
    }

    [Fact]
    public void DetectGreenLootPillars_ShouldIgnoreRightPartyUiArea()
    {
        using var mat = CreateEmptyFrame();
        Cv2.Rectangle(mat, new Rect(1700, 620, 22, 150), new Scalar(80, 255, 80), -1);

        var result = ScanPickTask.DetectGreenLootPillars(mat);

        Assert.Empty(result);
    }

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
        Assert.False(decision1080P.Left);
        Assert.True(decision1080P.Right);
        Assert.False(decision1080P.Forward);
        Assert.False(decision1080P.Backward);
    }

    private static Mat CreateEmptyFrame()
    {
        return new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(0));
    }
}
