using Xunit;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathing.Handler;

public class ActionFactoryTests
{
    [Fact]
    public void GetAfterHandler_WithValidAction_ReturnsHandler()
    {
        // Act
        var handler = ActionFactory.GetAfterHandler("nahida_collect");

        // Assert
        Assert.NotNull(handler);
        Assert.IsType<NahidaCollectHandler>(handler);
    }

    [Fact]
    public void GetAfterHandler_WithInvalidAction_ReturnsNull()
    {
        // Act
        var handler = ActionFactory.GetAfterHandler("invalid_action_code");

        // Assert
        Assert.Null(handler);
    }

    [Fact]
    public void GetBeforeHandler_WithValidAction_ReturnsHandler()
    {
        // Act
        var handler = ActionFactory.GetBeforeHandler("up_down_grab_leaf");

        // Assert
        Assert.NotNull(handler);
        Assert.IsType<UpDownGrabLeafHandler>(handler);
    }

    [Fact]
    public void GetBeforeHandler_WithInvalidAction_ReturnsNull()
    {
        // Act
        var handler = ActionFactory.GetBeforeHandler("invalid_action_code");

        // Assert
        Assert.Null(handler);
    }
}
