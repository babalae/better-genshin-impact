using Xunit;
using BetterGenshinImpact.GameTask.AutoPathing.Strategy;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathing.Strategy;

public class WaypointStrategyFactoryTests
{
    [Fact]
    public void GetStrategy_WhenTypeIsTeleport_ShouldReturnTeleportStrategy()
    {
        // Act
        var strategy = WaypointStrategyFactory.GetStrategy(WaypointType.Teleport.Code);

        // Assert
        Assert.NotNull(strategy);
        Assert.IsType<TeleportWaypointStrategy>(strategy);
    }

    [Theory]
    [InlineData("path")]
    [InlineData("target")]
    [InlineData("orientation")]
    public void GetStrategy_WhenTypeIsNotTeleport_ShouldReturnMovementStrategy(string code)
    {
        // Act
        var strategy = WaypointStrategyFactory.GetStrategy(code);

        // Assert
        Assert.NotNull(strategy);
        Assert.IsType<MovementWaypointStrategy>(strategy);
    }
}
