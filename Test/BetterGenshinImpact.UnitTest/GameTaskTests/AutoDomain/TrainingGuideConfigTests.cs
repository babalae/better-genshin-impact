using BetterGenshinImpact.GameTask.AutoDomain;
using Newtonsoft.Json;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomain;

public class TrainingGuideConfigTests
{
    [Fact]
    public void RewardRecognition_AllSwitchCombinationsPreserveLegacyGate()
    {
        foreach (var guide in new[] { false, true })
        foreach (var calculate in new[] { false, true })
        foreach (var planRewards in new[] { false, true })
        foreach (var taskRewards in new[] { false, true })
        {
            var config = new AutoDomainConfig
            {
                DevelopmentGuideCalculateRunsEnabled = calculate,
                DevelopmentGuideRewardRecognitionEnabled = planRewards
            };
            var domain = guide ? AutoDomainTask.TrainingGuideOption : "普通秘境";
            Assert.Equal(taskRewards || (guide && calculate && planRewards),
                config.ShouldRecognizeRewards(domain, taskRewards));
        }
    }

    [Theory]
    [InlineData(-1, -1, 0, 0)]
    [InlineData(2, 99, 0, 20)]
    [InlineData(1, 7, 1, 7)]
    public void Deserialization_NormalizesPlanningParameters(
        int preference, int percent, int expectedPreference, int expectedPercent)
    {
        var json = JsonConvert.SerializeObject(new
        {
            DevelopmentGuideRunPreference = preference,
            DevelopmentGuideCraftingBonusReservePercent = percent
        });
        var config = JsonConvert.DeserializeObject<AutoDomainConfig>(json)!;
        Assert.Equal(expectedPreference, config.DevelopmentGuideRunPreference);
        Assert.Equal(expectedPercent, config.DevelopmentGuideCraftingBonusReservePercent);
        var restored = JsonConvert.DeserializeObject<AutoDomainConfig>(JsonConvert.SerializeObject(config))!;
        Assert.Equal(expectedPreference, restored.DevelopmentGuideRunPreference);
        Assert.Equal(expectedPercent, restored.DevelopmentGuideCraftingBonusReservePercent);
    }
}
