using BetterGenshinImpact.GameTask.AutoPick;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class AutoPickTriggerTests
{
    [Theory]
    [InlineData("Mint", "Mint")]
    [InlineData("  Mint  ", "Mint")]
    [InlineData("[Mint]", "「Mint」")]
    [InlineData("「Mint」", "「Mint」")]
    [InlineData("Mint!", "Mint!")]
    [InlineData("[Mint!]", "「Mint!」")]
    [InlineData("𝒜Mint", "𝒜Mint")]
    [InlineData("Mint𝒜", "Mint𝒜")]
    [InlineData("𝒜", "𝒜")]
    [InlineData("𝟙Mint", "𝟙Mint")]
    [InlineData("Mint𝟙", "Mint𝟙")]
    [InlineData("𝟙", "𝟙")]
    public void ProcessOcrText_PreservesEnglishInteractionText(string input, string expected)
    {
        Assert.Equal(expected, AutoPickTrigger.ProcessOcrText(input));
    }
}
