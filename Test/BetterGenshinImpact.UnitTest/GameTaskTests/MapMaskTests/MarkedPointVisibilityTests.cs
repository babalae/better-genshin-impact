using BetterGenshinImpact.Model.MaskMap;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>
/// “隐藏已标记的标点”开关下点位是否绘制的测试
/// </summary>
public sealed class MarkedPointVisibilityTests
{
    /// <summary>开关关闭时已标记的点位仍绘制（半透明），开启时不绘制；未标记的点位始终绘制</summary>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void IsDrawn_HidesMarkedPointsOnlyWhenEnabled(bool isMarked, bool hideMarked, bool expected)
    {
        var point = new MaskMapPoint { IsHidden = isMarked };
        Assert.Equal(expected, point.IsDrawn(hideMarked));
    }
}
