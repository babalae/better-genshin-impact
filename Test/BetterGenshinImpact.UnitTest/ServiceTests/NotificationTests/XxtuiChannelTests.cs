using System.Reflection;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notifier;

namespace BetterGenshinImpact.UnitTest.ServiceTests.NotificationTests;

/// <summary>
/// xxtui 配置中的合法渠道应保留原值，不能被误判为无效渠道并回退到微信公众号。
/// </summary>
public class XxtuiChannelTests
{
    /// <summary>
    /// 覆盖官方支持的单渠道、多渠道及带空白的配置。
    /// </summary>
    [Theory]
    [InlineData("WX_MP", "WX_MP")]
    [InlineData("WX_QY_ROBOT", "WX_QY_ROBOT")]
    [InlineData("DING_ROBOT", "DING_ROBOT")]
    [InlineData("BARK", "BARK")]
    [InlineData("FEI_SHU_ROBOT", "FEI_SHU_ROBOT")]
    [InlineData("EMAIL", "EMAIL")]
    [InlineData("SMS_VOICE", "SMS_VOICE")]
    [InlineData("CUSTOM_HTTP", "CUSTOM_HTTP")]
    [InlineData("FEI_SHU_ROBOT,EMAIL", "FEI_SHU_ROBOT,EMAIL")]
    [InlineData(" WX_MP, FEI_SHU_ROBOT, EMAIL ", "WX_MP,FEI_SHU_ROBOT,EMAIL")]
    public void ParseXxtuiChannels_SupportedChannels_PreservesConfiguredDestinations(string configuration, string expected)
    {
        var channels = ParseChannels(configuration);

        Assert.Equal(expected, string.Join(",", channels));
    }

    /// <summary>
    /// 保持空配置和无效配置的既有默认行为。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("UNKNOWN_CHANNEL")]
    public void ParseXxtuiChannels_NoSupportedChannel_UsesDefault(string configuration)
    {
        Assert.Equal(new[] { XxtuiChannel.WX_MP }, ParseChannels(configuration));
    }

    /// <summary>
    /// 调用通知服务的真实配置解析入口，避免在测试中复制解析逻辑。
    /// </summary>
    private static XxtuiChannel[] ParseChannels(string configuration)
    {
        var method = typeof(NotificationService).GetMethod("ParseXxtuiChannels", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (XxtuiChannel[])method.Invoke(null, new object[] { configuration })!;
    }
}
