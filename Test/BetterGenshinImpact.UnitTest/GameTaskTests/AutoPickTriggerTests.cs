using BetterGenshinImpact.GameTask.AutoPick;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class AutoPickTriggerTests
{
    [Theory]
    [InlineData("Mint", "Mint")]
    [InlineData("  Mint  ", "Mint")]
    [InlineData("[Mint]", "「Mint」")]
    [InlineData("「Mint」", "「Mint」")]
    public void ProcessOcrText_PreservesEnglishInteractionText(string input, string expected)
    {
        Assert.Equal(expected, AutoPickTrigger.ProcessOcrText(input));
    }
}
