using BetterGenshinImpact.GameTask.MapMask;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>独立验证分层外观：共享地表背景能够几何定位，但不能因此确认当前选中的层。</summary>
public class BigMapLayerAppearanceTests(ITestOutputHelper output)
{
    /// <summary>输出外观相关度及灰度差，便于定位真实截图回归。</summary>
    private readonly ITestOutputHelper _output = output;

    /// <summary>真实选中层通过，只有公共背景一致的其它层必须被拒绝。</summary>
    [Theory]
    [InlineData("city-b1", "LayeredMap_29_-1", true)]
    [InlineData("city-b1", "LayeredMap_31_-1", false)]
    [InlineData("city-b2", "LayeredMap_29_-1", false)]
    [InlineData("city-b2", "LayeredMap_29_-2", true)]
    [InlineData("city-b1-east", "LayeredMap_31_-1", true)]
    [InlineData("mine-b1-failure", "LayeredMap_31_-1", true)]
    [InlineData("mine-b1-failure", "LayeredMap_29_-1", false)]
    [InlineData("mine-b1-failure-4k", "LayeredMap_31_-1", true)]
    [InlineData("city-b1-mid-new", "LayeredMap_32_-1", false)]
    [InlineData("city-b1-mid-new", "LayeredMap_29_-1", true)]
    public void CompareAppearance_DistinguishesSelectedLayerFromSharedBackground(string name, string layerId, bool selected)
    {
        using var image = MapMaskTestImages.Load(name);
        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        using var sift = SIFT.Create(6000);
        using var query = BigMapImageFeatures.Extract(sift, gray, 960);
        using var layerImage = new Mat(new BigMapLayerMatcher.LayerInfo { LayerId = layerId }.ImagePath, ImreadModes.Grayscale);
        Assert.False(layerImage.Empty());
        using var target = BigMapImageFeatures.Extract(sift, layerImage, 1536);
        var match = BigMapFeatureGeometry.Match(query, target.KeyPoints, target.Descriptors, 0.2, target.Size);
        Assert.NotNull(match);
        var appearance = BigMapFeatureGeometry.CompareAppearance(query, target, match.Viewport);
        _output.WriteLine($"{name} / {layerId}: {appearance}");
        Assert.Equal(selected, appearance.Correlation >= 0.75 && appearance.MeanDifference <= 30);
    }
}
