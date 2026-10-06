using BetterGenshinImpact.GameTask.InventoryMaterialStats;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.InventoryMaterialStatsTests;

public class InventoryMaterialStatsNameMatcherTests
{
    [Theory]
    [InlineData("涤凈青金", "涤净青金")]
    [InlineData("涤淨青金", "涤净青金")]
    [InlineData("怪木", "柽木")]
    public void Canonical_NormalizesSpecialVariants(string input, string expected)
    {
        Assert.Equal(expected, InventoryMaterialStatsNameMatcher.Canonical(input));
    }

    [Fact]
    public void MatchUnique_MapsVariantTitleToWhitelist()
    {
        var catalog = new[] { "涤净青金", "最胜紫晶", "柽木" };
        Assert.Equal("涤净青金", InventoryMaterialStatsNameMatcher.MatchUnique("涤凈青金", catalog, catalog));
        Assert.Equal("柽木", InventoryMaterialStatsNameMatcher.MatchUnique("怪木", catalog, catalog));
    }
}
