using BetterGenshinImpact.GameTask.AutoPick;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPickTests;

public class AutoPickTriggerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("şÉ")]
    public void ShouldFallbackToPaddleOcr_WhenYapTextCleansToNoUsablePickName(string rawText)
    {
        Assert.True(AutoPickTrigger.ShouldFallbackToPaddleOcr(rawText));
    }

    [Theory]
    [InlineData("薄荷")]
    [InlineData("调查")]
    [InlineData("「薄荷」")]
    public void ShouldNotFallbackToPaddleOcr_WhenYapTextContainsUsableChineseName(string rawText)
    {
        Assert.False(AutoPickTrigger.ShouldFallbackToPaddleOcr(rawText));
    }
}
