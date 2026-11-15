using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.MapMask;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>定位几何的独立测试，验证误匹配拒绝和单位换算，不依赖地图素材。</summary>
public class BigMapFeatureGeometryTests
{
    /// <summary>分散在查询图中的固定特征点。</summary>
    private static Point2f[] Grid() => Enumerable.Range(0, 20)
        .Select(i => new Point2f(40 + i % 5 * 160, 30 + i / 5 * 120)).ToArray();

    /// <summary>少量错误对应不能拉偏视口，最终尺寸必须保持截图宽高比。</summary>
    [Fact]
    public void Fit_ScaleTranslationWithOutliers_ReturnsFullViewport()
    {
        var source = Grid();
        var target = source.Select(p => new Point2f(p.X * 0.6f + 2400, p.Y * 0.6f + 700)).ToArray();
        target[0] = new Point2f(-9000, 7000);
        target[1] = new Point2f(12000, -5000);
        var result = BigMapFeatureGeometry.Fit(source, target, new Size(960, 540));
        Assert.NotNull(result);
        Assert.Equal(2400, result.Viewport.X, 2);
        Assert.Equal(700, result.Viewport.Y, 2);
        Assert.Equal(576, result.Viewport.Width, 2);
        Assert.Equal(324, result.Viewport.Height, 2);
        Assert.InRange(result.Confidence, 0.89, 0.91);
    }

    /// <summary>旋转、镜像、非等比缩放、点聚集和退化对应全部应拒绝。</summary>
    [Theory]
    [InlineData("rotate")]
    [InlineData("mirror")]
    [InlineData("stretch")]
    [InlineData("collapse")]
    [InlineData("cluster")]
    [InlineData("nonfinite")]
    public void Fit_InvalidGeometry_ReturnsNull(string kind)
    {
        var source = Grid();
        if (kind == "cluster")
        {
            source = source.Select(p => new Point2f(p.X / 100, p.Y / 100)).ToArray();
        }

        var target = source.Select(p => kind switch
        {
            "rotate" => new Point2f(p.X * 0.9f - p.Y * 0.3f, p.X * 0.3f + p.Y * 0.9f),
            "mirror" => new Point2f(-p.X, -p.Y),
            "stretch" => new Point2f(p.X * 0.4f, p.Y * 0.8f),
            "collapse" => new Point2f(123, 456),
            "nonfinite" => new Point2f(float.NaN, p.Y),
            _ => new Point2f(p.X * 0.6f + 400, p.Y * 0.6f + 200)
        }).ToArray();
        Assert.Null(BigMapFeatureGeometry.Fit(source, target, new Size(960, 540)));
    }

    /// <summary>不同层图 Scale 必须使用除法，并补偿特征提取时的缩小比例。</summary>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 0.5)]
    [InlineData(1.5, 0.25)]
    public void LayerViewport_ConvertsPixelsThroughWorldUnits(double scale, double imageScale)
    {
        var layer = new BigMapLayerMatcher.LayerInfo { Scale = (float)scale, Left = 5000, Top = 10000 };
        var rect = new Rect2d(120, 240, 900, 600);
        var result = layer.ToViewport2048(rect, imageScale, new TeyvatMap());
        Assert.Equal(rect.Width / scale / imageScale * 2, result.Width, 2);
        Assert.Equal(rect.Height / scale / imageScale * 2, result.Height, 2);
        Assert.Equal(32768 - (5000 - 120 / scale / imageScale) * 2, result.X, 2);
        Assert.Equal(24576 - (10000 - 240 / scale / imageScale) * 2, result.Y, 2);
    }
}
