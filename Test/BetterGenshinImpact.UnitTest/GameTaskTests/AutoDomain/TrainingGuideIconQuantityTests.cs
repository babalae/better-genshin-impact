using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomain;

public class TrainingGuideIconQuantityTests
{
    private static readonly TrainingGuideMaterial Material = TrainingGuideMaterialCatalog.Materials[0];

    [Theory]
    [InlineData("描述乱码\n可合成数量：乱码\n当前拥有 67", 67, 0, false)]
    [InlineData("培养需求 0／4", 0, 4, true)]
    [InlineData("来源错误\n培养需求 17 / 9", 17, 9, true)]
    public void CountsDoNotDependOnAuxiliaryText(string text, int stock, int required, bool target)
    {
        var reading = TrainingGuidePopupParser.ParseQuantities(Material, text);
        Assert.NotNull(reading);
        Assert.Equal(Material, reading.Material);
        Assert.Equal(stock, reading.Stock);
        Assert.Equal(required, reading.Required);
        Assert.Equal(target, reading.IsTarget);
    }

    [Theory]
    [InlineData("可合成数量：67")]
    [InlineData("培养需求 /4")]
    [InlineData("培养需求 0/0")]
    [InlineData("当前拥有 999999999999999999")]
    [InlineData("当前拥有 4\n培养需求 4/9")]
    [InlineData("当前拥有 4\n当前拥有 5")]
    [InlineData("当前拥有 4X")]
    public void MissingAmbiguousOrInvalidCountsAreRejected(string text) =>
        Assert.Null(TrainingGuidePopupParser.ParseQuantities(Material, text));
}
