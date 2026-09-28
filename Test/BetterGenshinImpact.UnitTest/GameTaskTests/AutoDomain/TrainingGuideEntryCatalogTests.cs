using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomain;

public class TrainingGuideEntryCatalogTests
{
    [Fact]
    public void EntriesCoverEveryFamilyOnce()
    {
        Assert.Equal(48, TrainingGuideEntryCatalog.Entries.Count);
        Assert.Equal(TrainingGuideMaterialCatalog.Materials.Select(m => m.Family).Distinct().Order(),
            TrainingGuideEntryCatalog.Entries.Select(e => e.Family).Order());
        foreach (var entry in TrainingGuideEntryCatalog.Entries)
        {
            Assert.Same(entry, TrainingGuideEntryCatalog.Find(entry.Domain, entry.Entry + " IV"));
            var count = entry.IsWeapon ? 4 : 3;
            for (var i = 0; i < count; i++)
                Assert.Equal(count - i - 1, entry.MaterialAt(i, count)?.Tier);
            Assert.Null(entry.MaterialAt(0, count - 1));
        }
    }

    [Theory]
    [InlineData("贡祭炽心的")]
    [InlineData("贡祭炽心的铸曙")]
    public void IncompleteTitleUsesEntryButKeepsActualNumbers(string title)
    {
        var entry = TrainingGuideEntryCatalog.Find("深古瞭望所", "炼武秘境:冥见Ⅳ")!;
        var reading = TrainingGuidePopupParser.Parse(title, "武器突破素材", "可合成数量：14\n当前拥有67",
            "炼武秘境：冥见（限时开放）", entry.MaterialAt(2, 4), entry.Entry);
        Assert.NotNull(reading);
        Assert.Equal("贡祭炽心的踌躇", reading.Material.Name);
        Assert.Equal(67, reading.Stock);
        Assert.Equal(14, reading.Craftable);
        Assert.False(reading.IsTarget);
    }

    [Theory]
    [InlineData("贡祭炽心的决绝", "炼武秘境：冥见", "当前拥有67")]
    [InlineData("贡祭炽心的", "炼武秘境：究观", "当前拥有67")]
    [InlineData("贡祭炽心的", "", "当前拥有67")]
    [InlineData("", "炼武秘境：冥见", "当前拥有67")]
    [InlineData("谵妄圣主的", "炼武秘境：冥见", "当前拥有67")]
    [InlineData("贡祭炽心的", "炼武秘境：冥见", "可合成数量：14")]
    [InlineData("贡祭炽心的", "炼武秘境：冥见", "培养需求/4")]
    public void FallbackRejectsConflictsAndMissingNumbers(string title, string source, string footer)
    {
        var entry = TrainingGuideEntryCatalog.Find("深古瞭望所", "炼武秘境：冥见IV")!;
        Assert.Null(TrainingGuidePopupParser.Parse(title, "武器突破素材", footer, source,
            entry.MaterialAt(2, 4), entry.Entry));
    }
}
