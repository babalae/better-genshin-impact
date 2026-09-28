using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomain;

public class TrainingGuideMaterialRecognitionTests
{
    [Fact]
    public void MixedResinPlan_RespectsSharedAllowanceAndPerFamilyRounding()
    {
        var plan = TrainingGuideResinPlanner.Allocate([20, 80],
            [new("浓缩", 60, 1), new("原粹40", 40, 1), new("原粹20", 20, 1)]);
        Assert.Equal(120, plan.Resin);
        Assert.Equal(20, plan.UncoveredResin);
        Assert.Equal(3, plan.Uses.Count);
    }

    [Fact]
    public void WeaponDrops_UseHighestAdventureRankExpectations()
    {
        var readings = TrainingGuideMaterialCatalog.Materials.Where(m => m.Family == "凛雪帝皇")
            .Select(m => new TrainingGuideMaterialReading(m, 0, m.Tier == 3 ? 4 : 0, null, m.Tier == 3, "")).ToArray();
        Assert.Equal(160, TrainingGuideResinEstimator.Estimate(readings, 0, 20));
        Assert.Equal(180, TrainingGuideResinEstimator.Estimate(readings, 0, 60));
    }

    [Fact]
    public void Catalog_AllFamiliesHaveUniqueContiguousTiers()
    {
        Assert.Equal(168, TrainingGuideMaterialCatalog.Materials.Count);
        foreach (var family in TrainingGuideMaterialCatalog.Materials.GroupBy(m => m.Family))
        {
            Assert.Equal(Enumerable.Range(0, family.First().IsWeapon ? 4 : 3), family.Select(m => m.Tier).Order());
            foreach (var item in family) Assert.Equal(item, TrainingGuideMaterialCatalog.Find(item.Name));
        }
    }

    [Theory]
    [InlineData("培养需求 0/4\n可合成数量：2", 0, 4, 2)]
    [InlineData("培养需求 17 / 9\n可合成数量: 0", 17, 9, 0)]
    public void Popup_ParsesActualStockWithoutCapping(string footer, int stock, int required, int craft)
    {
        var reading = TrainingGuidePopupParser.Parse("凛雪帝皇的辞诀", "武器突破素材", footer, "炼武秘境：断钢");
        Assert.NotNull(reading);
        Assert.Equal(stock, reading.Stock);
        Assert.Equal(required, reading.Required);
        Assert.Equal(craft, reading.Craftable);
    }

    [Theory]
    [InlineData("培养需求 /4")]
    [InlineData("培养需求 0/四")]
    [InlineData("可合成数量:2")]
    [InlineData("培养需求 999999999999999/4")]
    public void Popup_MissingOrInvalidStockNeverBecomesZero(string footer) =>
        Assert.Null(TrainingGuidePopupParser.Parse("凛雪帝皇的辞决", "武器突破素材", footer, ""));

    [Fact]
    public void DecimalDrops_AreAccumulatedBeforeRounding()
    {
        var family = TrainingGuideMaterialCatalog.Materials.Where(m => m.Family == "「自由」").OrderBy(m => m.Tier)
            .Select(m => new TrainingGuideMaterialReading(m, 0, m.Tier == 2 ? 1 : 0, null, m.Tier == 2, "")).ToArray();
        Assert.Equal(40, TrainingGuideResinEstimator.Estimate(family, 0, 20));
        Assert.Equal(40, TrainingGuideResinEstimator.Estimate(family, 0, 40));
        Assert.Equal(60, TrainingGuideResinEstimator.Estimate(family, 0, 60));
        Assert.Null(TrainingGuideResinEstimator.Estimate(family.Skip(1).ToArray(), 0, 20));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void AllOpenOrder_MapsToBgiSelection(int position, int expected) =>
        Assert.Equal(expected, TrainingGuideMaterialCatalog.SelectionIndex(position));
}
