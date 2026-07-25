using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using System.Text.Json;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathingTests;

public class RewardEndDetectionConfigTests
{
    [Theory]
    [InlineData("experience")]
    [InlineData("exp")]
    [InlineData("经验值")]
    public void TryCreate_ExperienceAlias_EnablesExperienceDetection(string rewardType)
    {
        Assert.True(RewardEndDetectionConfig.TryCreate(rewardType, [400], out var config));
        Assert.NotNull(config);
        Assert.Equal(RewardEndDetectionType.Experience, config.Type);
        Assert.Null(config.MoraValues);
    }

    [Fact]
    public void TryCreate_MoraValues_OnlyEnablesConfiguredTemplates()
    {
        Assert.True(RewardEndDetectionConfig.TryCreate("mora", [400, 600], out var config));
        Assert.NotNull(config);
        Assert.True(config.IsMoraValueEnabled(400));
        Assert.True(config.IsMoraValueEnabled(600));
        Assert.False(config.IsMoraValueEnabled(1200));
    }

    [Fact]
    public void TryCreate_MoraWithoutValues_EnablesAllMoraTemplates()
    {
        Assert.True(RewardEndDetectionConfig.TryCreate("摩拉", null, out var config));
        Assert.NotNull(config);
        Assert.True(config.IsMoraValueEnabled(400));
        Assert.True(config.IsMoraValueEnabled(3000));
    }

    [Fact]
    public void WaypointJson_DeserializesFightRewardExtensionParameters()
    {
        const string json = """
                            {
                              "x": 1,
                              "y": 2,
                              "action": "fight",
                              "point_ext_params": {
                                "auto_fight": {
                                  "reward_type": "mora",
                                  "mora_values": [400, 600]
                                }
                              }
                            }
                            """;

        var waypoint = JsonSerializer.Deserialize<Waypoint>(json, PathRecorder.JsonOptions);
        var autoFight = waypoint?.PointExtParams.AutoFight;

        Assert.NotNull(autoFight);
        Assert.Equal("mora", autoFight.RewardType);
        Assert.Equal([400, 600], autoFight.MoraValues);
    }
}