using System.Globalization;
using BetterGenshinImpact.GameTask.I18n;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.ResinTests;

/// <summary>仅构造 OCR 区域验证领奖行匹配，不点击、不初始化游戏运行环境。</summary>
public sealed class ResinRewardPromptTests
{
    /// <summary>原粹显示补充时仍可使用其他树脂；拆分名称与消耗量仍能按行识别。</summary>
    [Fact]
    public void Offered_ExcludesReplenishRowAndKeepsOtherKinds()
    {
        Region[] rows = [Text(100, 100, "浓缩"), Text(160, 100, "树脂"), Text(800, 100, "使用"),
            Text(100, 200, "原粹树脂20"), Text(800, 200, "补充"),
            Text(100, 300, "须臾树脂"), Text(800, 300, "使用"),
            Text(100, 400, "脆弱树脂"), Text(800, 400, "使用")];
        var offered = ResinRewardPrompt.DetectOffered(rows, 500, new CultureInfo("zh-Hans"), GameText);
        Assert.Equal(new[] { ResinKind.Condensed, ResinKind.Transient, ResinKind.Fragile }, ResinKinds.InOrder.Where(offered.Contains));
        Assert.True(ResinRewardPrompt.HasShortageHint(rows, new CultureInfo("zh-Hans"), GameText));
    }

    /// <summary>使用按钮必须位于右侧且与名称行相交，其他行或左侧文字不能当成可用按钮。</summary>
    [Fact]
    public void Offered_RequiresRightHandButtonOnSameRow()
    {
        Region[] rows = [Text(100, 100, "原粹树脂40"), Text(800, 200, "使用"), Text(200, 100, "使用")];
        Assert.Empty(ResinRewardPrompt.DetectOffered(rows, 500, CultureInfo.InvariantCulture, GameText));
        Assert.False(ResinRewardPrompt.IsHeightOverlap(rows[0], rows[1]));
        Assert.True(ResinRewardPrompt.IsHeightOverlap(rows[0], rows[2]));
    }

    /// <summary>按游戏语言使用对应的游戏文本匹配名称和按钮；en-US 按父区域 en 查找。</summary>
    [Fact]
    public void Offered_UsesGameTextForTranslatedNamesAndButtons()
    {
        Region[] rows = [Text(100, 100, "Original Resin 40"), Text(800, 100, "Use")];
        Assert.Equal(new[] { ResinKind.Original }, ResinRewardPrompt.DetectOffered(rows, 500, new CultureInfo("en-US"), GameText));
    }

    /// <summary>原粹点数识别及默认值保持旧逻辑，非原粹的实际个数由选择器记账。</summary>
    [Theory]
    [InlineData("原粹树脂20", ResinKind.Original, 20)]
    [InlineData("原粹树脂40", ResinKind.Original, 40)]
    [InlineData("原粹树脂 200/200", ResinKind.Original, 20)]
    [InlineData("浓缩树脂", ResinKind.Condensed, 0)]
    public void OriginalSpent_ReadsCostOrFallsBack(string text, ResinKind kind, int expected)
        => Assert.Equal(expected, ResinRewardPrompt.GetResinNum(Text(0, 0, text), kind, NullLogger.Instance, "测试"));

    /// <summary>建立脱离截图树的 OCR 区域。</summary>
    private static Region Text(int x, int y, string text) => new(x, y, 80, 20) { Text = text };

    /// <summary>使用测试输出目录中的游戏文本表，不依赖服务容器和配置。</summary>
    private static readonly GameTextService GameText = new(Path.Combine(AppContext.BaseDirectory, "GameTask", "I18n"));
}
