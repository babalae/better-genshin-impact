using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoFight;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomainTests;

public class AutoDomainFightSeekOptionsTests
{
    [Fact]
    public void FromAutoFightConfig_ShouldDisableAssistWhenRotateFindEnemyIsOff()
    {
        var config = new AutoFightConfig();
        config.FinishDetectConfig.RotateFindEnemyEnabled = false;

        var options = AutoDomainFightSeekOptions.FromAutoFightConfig(config);

        Assert.False(options.Enabled);
    }

    [Fact]
    public void FromAutoFightConfig_ShouldUseDefaultSeekIntervalWhenFastCheckIsOff()
    {
        var config = new AutoFightConfig();
        config.FinishDetectConfig.RotateFindEnemyEnabled = true;
        config.FinishDetectConfig.FastCheckEnabled = false;
        config.FinishDetectConfig.FastCheckParams = "1";
        config.FinishDetectConfig.RotaryFactor = 12;

        var options = AutoDomainFightSeekOptions.FromAutoFightConfig(config);

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(3), options.Interval);
        Assert.Equal(12, options.RotaryFactor);
    }

    [Fact]
    public void FromAutoFightConfig_ShouldClampFastCheckIntervalAndRotaryFactor()
    {
        var config = new AutoFightConfig();
        config.FinishDetectConfig.RotateFindEnemyEnabled = true;
        config.FinishDetectConfig.FastCheckEnabled = true;
        config.FinishDetectConfig.FastCheckParams = "0.2;钟离;";
        config.FinishDetectConfig.RotaryFactor = 99;
        config.FinishDetectConfig.IsFirstCheck = true;

        var options = AutoDomainFightSeekOptions.FromAutoFightConfig(config);

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Interval);
        Assert.Equal(13, options.RotaryFactor);
        Assert.Equal(TimeSpan.Zero, options.InitialDelay);
    }
}
