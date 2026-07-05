using BetterGenshinImpact.GameTask.Common.Job;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.CommonJobTests;

public class SwitchCharacterStateMachineTaskTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(" 胡桃 ", "胡桃")]
    public void NormalizeSlotName_ShouldTreatNullAndWhitespaceAsSkippedSlot(string? value, string expected)
    {
        Assert.Equal(expected, SwitchCharacterStateMachineTask.NormalizeSlotName(value));
    }

    [Fact]
    public void WrapSwitchCharacterException_ShouldPreserveBusinessFalseBoundary()
    {
        var inner = new InvalidOperationException("角色名称校验失败：不存在的角色");

        var exception = SwitchCharacterStateMachineTask.WrapSwitchCharacterException(inner);

        Assert.Contains("角色名称校验失败", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }
}
