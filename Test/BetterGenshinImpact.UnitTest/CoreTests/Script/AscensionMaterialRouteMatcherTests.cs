using BetterGenshinImpact.Core.Script;

namespace BetterGenshinImpact.UnitTest.CoreTests.Script;

public class AscensionMaterialRouteMatcherTests
{
    /// <summary>Verifies duplicate material names merge their distinct acquisition sources.</summary>
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

    /// <summary>Verifies exact material-folder and enemy-source matches.</summary>
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

    /// <summary>Verifies broad region names do not match specific source text.</summary>
    [Fact]
    public void DoesNotMatchBroadRegionFolderAgainstSourceText()
    {
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["稚嫩的尖齿"] = ["纳塔龙众掉落"],
        };

        Assert.Empty(AscensionMaterialRouteMatcher.FindRelatedMaterials("纳塔", ["稚嫩的尖齿"], sources));
    }

    /// <summary>Verifies short unrelated folder names do not create false matches.</summary>
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
