using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.UnitTest.CoreTests.ConfigTests;

public class OneDragonFlowConfigTests
{
    [Fact]
    public void CloseAdventurerHandbookAfterDailyRewardCheck_DefaultsToTrue()
    {
        var config = new OneDragonFlowConfig();

        Assert.True(config.CloseAdventurerHandbookAfterDailyRewardCheck);
    }

    [Fact]
    public void CloseAdventurerHandbookAfterDailyRewardCheck_CanBeDisabled()
    {
        var config = new OneDragonFlowConfig
        {
            CloseAdventurerHandbookAfterDailyRewardCheck = false
        };

        Assert.False(config.CloseAdventurerHandbookAfterDailyRewardCheck);
    }
}
