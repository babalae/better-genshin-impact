using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomain;

public class TrainingGuideCustomTargetsTests
{
    [Fact]
    public void MissingTargetsKeepsGamePlan() => Assert.Null(TrainingGuideCustomTargets.Parse(null));

    [Fact]
    public void AcceptsMultipleTiersAndFamilies()
    {
        var targets = TrainingGuideCustomTargets.Parse("""
            [{"material":"「诤言」的哲学","target":28},
             {"material":"「诤言」的指引","target":12},
             {"material":"贡祭炽心的荣膺","target":4}]
            """)!;
        Assert.Equal(3, targets.Count);
        Assert.Equal(28, targets[TrainingGuideMaterialCatalog.Find("「诤言」的哲学")!]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[{\"material\":\"未知\",\"target\":1}]")]
    [InlineData("[{\"material\":\"「诤言」的哲学\",\"target\":0}]")]
    [InlineData("[{\"material\":\"「诤言」的哲学\",\"target\":-1}]")]
    [InlineData("[{\"material\":\"「诤言」的哲学\",\"target\":1.5}]")]
    [InlineData("[{\"material\":\"「诤言」的哲学\",\"target\":\"28\"}]")]
    [InlineData("[{\"material\":\"「诤言」的哲学\",\"target\":2147483648}]")]
    [InlineData("[{\"material\":\"「诤言」的哲学\",\"target\":1},{\"material\":\"「净言」的哲学\",\"target\":2}]")]
    public void RejectsInvalidTargets(string json) =>
        Assert.ThrowsAny<System.Exception>(() => TrainingGuideCustomTargets.Parse(json));
}
