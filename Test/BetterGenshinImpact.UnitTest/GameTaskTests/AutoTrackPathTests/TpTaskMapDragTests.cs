using BetterGenshinImpact.GameTask.AutoTrackPath;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoTrackPathTests;

public class TpTaskMapDragTests
{
    [Theory]
    [InlineData(100, -200, 100, -200)]
    [InlineData(600, 800, 180, 240)]
    [InlineData(-600, -800, -180, -240)]
    public void LimitMapDragDelta_ShouldPreserveDirectionAndCapDistance(
        int deltaX,
        int deltaY,
        int expectedX,
        int expectedY)
    {
        var actual = TpTask.LimitMapDragDelta(deltaX, deltaY);

        Assert.Equal((expectedX, expectedY), actual);
    }

    [Fact]
    public void IsMapMoveRecognitionAnomaly_ShouldRejectOrdinaryReverseMovement()
    {
        var actual = TpTask.IsMapMoveRecognitionAnomaly(
            expectedMoveLen: 300,
            actualMoveLen: 300,
            moveRatio: 1,
            moveDirectionCos: -1,
            jumpDistance: 600);

        Assert.True(actual);
    }

    [Fact]
    public void IsMapMoveRecognitionAnomaly_ShouldRejectNoProgress()
    {
        var actual = TpTask.IsMapMoveRecognitionAnomaly(
            expectedMoveLen: 300,
            actualMoveLen: 0,
            moveRatio: 0,
            moveDirectionCos: 1,
            jumpDistance: 300);

        Assert.True(actual);
    }

    [Fact]
    public void IsMapMoveRecognitionAnomaly_ShouldAcceptConsistentSmallMovement()
    {
        var actual = TpTask.IsMapMoveRecognitionAnomaly(
            expectedMoveLen: 120,
            actualMoveLen: 108,
            moveRatio: 0.9,
            moveDirectionCos: 0.98,
            jumpDistance: 15);

        Assert.False(actual);
    }
}
