using BetterGenshinImpact.GameTask.AutoDomain;
using Newtonsoft.Json;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomain;

public class TrainingGuideConfigTests
{
    [Fact]
    public void RewardRecognition_AllSwitchCombinationsCoverGameAndCustomTargets()
    {
        foreach (var guide in new[] { false, true })
        foreach (var calculate in new[] { false, true })
        foreach (var planRewards in new[] { false, true })
        foreach (var taskRewards in new[] { false, true })
        foreach (var customTargets in new[] { false, true })
        {
            // 构造函数读取全局游戏配置；本测试仅调用纯参数判断，显式初始化它依赖的全部字段。
            var param = (AutoDomainParam)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AutoDomainParam));
            param.DomainName = guide ? AutoDomainTask.TrainingGuideOption : "普通秘境";
            param.TrainingGuideCalculateRunsEnabled = calculate;
            param.TrainingGuideRewardRecognitionEnabled = planRewards;
            param.RewardRecognitionEnabled = taskRewards;
            param.TrainingTargetsJson = customTargets
                ? "[{\"material\":\"「诤言」的哲学\",\"target\":28}]" : null;
            Assert.Equal(taskRewards || (planRewards && (customTargets || (guide && calculate))),
                param.ShouldRecognizeRewards());
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
