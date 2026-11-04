using BetterGenshinImpact.GameTask.AutoPick;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class AutoPickInteractionFilterTests
{
    [Theory]
    [InlineData("开启导航视界")]
    [InlineData("打开枪械视界")]
    [InlineData("開啟導航視界")]
    [InlineData("開啟槍械視界")]
    public void ShouldNotPick_ViewInteractionPrompt_ReturnsTrue(string text)
    {
        Assert.True(AutoPickTrigger.ShouldNotPick(text));
    }

    [Theory]
    [InlineData("薄荷")]
    [InlineData("破损的面具")]
    public void ShouldNotPick_NormalPickupItem_ReturnsFalse(string text)
    {
        Assert.False(AutoPickTrigger.ShouldNotPick(text));
    }
}
