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
            var materials = TrainingGuideMaterialCatalog.Materials.Where(m => m.Family == entry.Family).ToArray();
            Assert.Equal(count, materials.Length);
            Assert.All(materials, m => Assert.Equal(entry.IsWeapon, m.IsWeapon));
        }
    }
}
