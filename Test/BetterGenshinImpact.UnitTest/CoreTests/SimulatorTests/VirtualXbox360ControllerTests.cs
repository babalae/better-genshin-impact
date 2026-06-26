using BetterGenshinImpact.Core.Simulator;

namespace BetterGenshinImpact.UnitTest.CoreTests.SimulatorTests;

public class VirtualXbox360ControllerTests
{
    [Fact]
    public void NormalizeYHoldMillisecondsUsesPickupDefaultWhenNoOverrideIsProvided()
    {
        Assert.True(VirtualXbox360Controller.DefaultPickYHoldMilliseconds >= 140);
        Assert.Equal(VirtualXbox360Controller.DefaultPickYHoldMilliseconds,
            VirtualXbox360Controller.NormalizeYHoldMilliseconds(null));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(29, 30)]
    [InlineData(30, 30)]
    [InlineData(80, 80)]
    [InlineData(160, 160)]
    public void NormalizeYHoldMillisecondsClampsOnlyTooShortOverrides(int requestedMilliseconds, int expectedMilliseconds)
    {
        Assert.Equal(expectedMilliseconds,
            VirtualXbox360Controller.NormalizeYHoldMilliseconds(requestedMilliseconds));
    }
}
