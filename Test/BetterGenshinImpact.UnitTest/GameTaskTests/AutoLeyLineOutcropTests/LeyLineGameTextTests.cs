using System.Globalization;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoLeyLineOutcropTests;

/// <summary>
/// 验证地脉花游戏文字匹配模式按游戏语言从真实资源解析，并在缺少资源时保持中文行为。
/// </summary>
public sealed class LeyLineGameTextTests
{
    /// <summary>
    /// 中文客户端：资源缺失时回退原有中文模式。
    /// </summary>
    [Fact]
    public void ChineseClient_KeepsChinesePatterns()
    {
        var text = new LeyLineGameText(new CultureInfo("zh-Hans"), CreateLocalizer());

        Assert.Matches(text.Country("蒙德"), "蒙德");
        Assert.Matches(text.Country("挪德卡莱"), "挪德卡");
        Assert.DoesNotMatch(text.Country("蒙德"), "Mondstadt");
        Assert.Matches(text.Flower, "地脉之花");
        Assert.Matches(text.RewardPromptContent, "使用原粹树脂");
        Assert.Matches(text.UseAction, "使用");
        Assert.Matches(text.Revive, "复苏");
        Assert.Matches(text.Fight, "打倒所有敌人");
        Assert.Matches(text.DoubleReward, "2倍");
    }

    /// <summary>
    /// 英文客户端：从 AutoDomainTask.en.resx 读取英文模式，兼容 OCR 丢失空格与大小写差异。
    /// </summary>
    [Fact]
    public void EnglishClient_UsesEnglishPatterns()
    {
        var text = new LeyLineGameText(new CultureInfo("en"), CreateLocalizer());

        Assert.Matches(text.Country("蒙德"), "Mondstadt");
        Assert.Matches(text.Country("挪德卡莱"), "Nod-Krai");
        Assert.DoesNotMatch(text.Country("蒙德"), "蒙德");
        Assert.Matches(text.Outcrop, "Ley Line Outcrop");
        Assert.Matches(text.Flower, "Ley LineBlossom");
        Assert.Matches(text.RewardPromptTitle, "Activate Ley Line Blossom");
        Assert.Matches(text.RewardPromptContent, "Original Resin");
        Assert.Matches(text.UseAction, "Use");
        Assert.Matches(text.OriginalResin, "Original Resin x40");
        Assert.Matches(text.Revive, "Revive");
        Assert.Matches(text.StopTracking, "Stop Navigation");
        Assert.Matches(text.Fight, "Defeat all opponents");
        Assert.Matches(new System.Text.RegularExpressions.Regex(text.InteractPrompt), "ley line blossom");
    }

    /// <summary>
    /// 使用真实的 resx 资源创建秘境本地化器。
    /// </summary>
    private static IStringLocalizer<AutoDomainTask> CreateLocalizer()
    {
        var factory = new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<AutoDomainTask>(factory);
    }
}
