using BetterGenshinImpact.GameTask.AutoDomain;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomainTests;

public class PetrifiedTreeCameraTests
{
    [Theory]
    [InlineData(960, 1920, 0)]
    [InlineData(865, 1920, 0)]
    [InlineData(1055, 1920, 0)]
    [InlineData(496, 1920, -44)]
    [InlineData(1424, 1920, 44)]
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
}
