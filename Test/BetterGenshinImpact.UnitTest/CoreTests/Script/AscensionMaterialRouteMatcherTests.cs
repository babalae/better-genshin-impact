using BetterGenshinImpact.Core.Script;

namespace BetterGenshinImpact.UnitTest.CoreTests.Script;

public class AscensionMaterialRouteMatcherTests
{
    [Fact]
    public void MergesDuplicateMaterialCatalogNamesAndCombinesSources()
    {
        var catalog = AscensionMaterialRouteMatcher.BuildMaterialSourceCatalog(
        [
            ("Vesna's Bloom", new string?[] { "Gathered in the wild" }),
            ("vesna's bloom", new string?[] { "Sold by a merchant", "Gathered in the wild" }),
        ]);

        Assert.Single(catalog);
        Assert.Equal(2, catalog["VESNA'S BLOOM"].Count);
        Assert.Contains("Gathered in the wild", catalog["Vesna's Bloom"]);
        Assert.Contains("Sold by a merchant", catalog["Vesna's Bloom"]);
    }

    [Fact]
    public void FindsExactMaterialFolderAndMultipleSourceFolders()
    {
        var materials = new[] { "云岩裂叶", "稚嫩的尖齿", "老练的坚齿" };
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["云岩裂叶"] = ["推荐：安饶之野采集"],
            ["稚嫩的尖齿"] = ["纳塔龙众掉落"],
            ["老练的坚齿"] = ["40级以上纳塔龙众掉落"],
        };

        var specialtyMatch = AscensionMaterialRouteMatcher.FindRelatedMaterials("云岩裂叶", materials, sources);
        Assert.Single(specialtyMatch);
        Assert.Contains("云岩裂叶", specialtyMatch);

        var enemyDropMatches = AscensionMaterialRouteMatcher.FindRelatedMaterials("纳塔龙众", materials, sources);
        Assert.Equal(2, enemyDropMatches.Count);
        Assert.Contains("稚嫩的尖齿", enemyDropMatches);
        Assert.Contains("老练的坚齿", enemyDropMatches);
    }

    [Fact]
    public void DoesNotMatchBroadRegionFolderAgainstSourceText()
    {
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["稚嫩的尖齿"] = ["纳塔龙众掉落"],
        };

        Assert.Empty(AscensionMaterialRouteMatcher.FindRelatedMaterials("纳塔", ["稚嫩的尖齿"], sources));
    }

    [Fact]
    public void DoesNotMatchShortUnrelatedFolders()
    {
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mora"] = ["Obtainable from world bosses"],
        };

        Assert.Empty(AscensionMaterialRouteMatcher.FindRelatedMaterials("HP", ["Mora"], sources));
    }
}
