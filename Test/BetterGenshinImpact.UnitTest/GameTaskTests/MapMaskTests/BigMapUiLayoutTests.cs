using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using OpenCvSharp;
using OpenCvSharp.Features2D;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>右侧面板的遮挡识别及下游坐标、特征和图标边界回归。</summary>
public class BigMapUiLayoutTests
{
    /// <summary>浅色地形不能误裁；传送详情、标记面板在三种分辨率都应找到边界。</summary>
    [Theory]
    [InlineData("city-waypoint-popup", true)]
    [InlineData("city-waypoint-popup-4k", true)]
    [InlineData("mine-marker-popup", true)]
    [InlineData("city-surface", false)]
    [InlineData("city-b1-close-new", false)]
    [InlineData("city-b1-mid-new", false)]
    [InlineData("city-b1-zoom-new", false)]
    [InlineData("inazuma-surface", false)]
    [InlineData("inazuma-zoom-near", false)]
    public void GetMapArea_RealScreenshots_FindsOnlyPopup(string name, bool popup)
    {
        using var source = MapMaskTestImages.Load(name);
        foreach (var width in new[] { 960, 1920, 3840 })
        {
            using var resized = new Mat();
            Cv2.Resize(source, resized, new Size(width, width * 9 / 16));
            var area = BigMapUiLayout.GetMapArea(resized);
            Assert.Equal(0, area.X);
            Assert.Equal(0, area.Y);
            Assert.Equal(resized.Height, area.Height);
            if (popup)
            {
                Assert.InRange(area.Width / (double)width, 0.742, 0.748);
            }
            else
            {
                Assert.Equal(width, area.Width);
            }
        }
    }

    /// <summary>排除遮挡时仍保留整屏尺寸，面板内的文字和图标不产生地图特征。</summary>
    [Fact]
    public void Prepare_Popup_ExcludesPanelFeaturesWithoutCroppingCoordinates()
    {
        using var image = MapMaskTestImages.Load("city-waypoint-popup-4k");
        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        using var query = BigMapImageFeatures.Prepare(gray, 960, BigMapUiLayout.GetMapArea(gray));
        using var sift = SIFT.Create(6000);
        query.EnsureFeatures(sift);
        Assert.Equal(new Size(960, 540), query.Size);
        Assert.InRange(query.MapArea.Width, 712, 718);
        Assert.True(query.KeyPoints.Length > 100);
        Assert.All(query.KeyPoints, p => Assert.True(p.Pt.X < query.MapArea.Right));
        using var matcher = new BigMapAnchorMatcher(new TeyvatMap());
        var anchors = matcher.Detect(query.Appearance, query.MapArea);
        Assert.NotEmpty(anchors);
        Assert.All(anchors, p => Assert.True(p.X < query.MapArea.Right - 50));
    }
}
