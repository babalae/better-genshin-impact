using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.MapMask;
using OpenCvSharp;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>真实稻妻截图，只调用图标识别和离线锚点几何，不使用 SIFT。</summary>
public class BigMapAnchorMatcherTests(ITestOutputHelper output)
{
    /// <summary>记录实际锚点数量及视口。</summary>
    private readonly ITestOutputHelper _output = output;

    /// <summary>L3 必须独立定位真实截图，不能依赖前一轮 L4/L5 成功。</summary>
    [Theory]
    [InlineData(960)]
    [InlineData(1920)]
    public void Match_RealInazumaMap_WithoutSift_ReturnsViewport(int width)
    {
        using var source = MapMaskTestImages.Load("inazuma-surface");
        using var color = new Mat();
        using var gray = new Mat();
        Cv2.Resize(source, color, new Size(width, width * 9 / 16));
        Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        using var matcher = new BigMapAnchorMatcher(new TeyvatMap());
        var anchors = matcher.Detect(gray);
        _output.WriteLine($"anchors: {anchors.Length}");
        Assert.True(anchors.Length >= 10);
        var result = matcher.Match(gray, null);
        Assert.NotNull(result);
        _output.WriteLine(result.ToString());
        Assert.InRange(result.Viewport.X, 36320, 36355);
        Assert.InRange(result.Viewport.Y, 30970, 31005);
        Assert.InRange(result.Viewport.Width, 5730, 5790);
        Assert.InRange(result.Confidence, 0.8, 1);
    }

    /// <summary>空白截图和锚点数据尚未覆盖的新矿区不应强行给出全局定位。</summary>
    [Fact]
    public void Match_NoCorrespondingAnchors_ReturnsNull()
    {
        using var matcher = new BigMapAnchorMatcher(new TeyvatMap());
        using var blank = new Mat(540, 960, MatType.CV_8UC1, Scalar.Gray);
        Assert.Null(matcher.Match(blank, null));
        using var source = MapMaskTestImages.Load("mine-b1-failure");
        using var gray = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        Assert.Null(matcher.Match(gray, null));
        Assert.Contains("检测到", matcher.LastDiagnostics);
        Assert.Contains("离线参考", matcher.LastDiagnostics);
    }

    /// <summary>新地区通过地形确认后保存本地参考，重新创建 L3 即可独立定位，不调用 SIFT。</summary>
    [Fact]
    public void Match_PersistedRuntimeAnchors_ReturnsViewportWithoutSift()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BetterGI-MapAnchorTest-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "anchors.json");
        try
        {
            using var source = MapMaskTestImages.Load("mine-runtime-overlay");
            using var gray = new Mat();
            Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
            using (var cascade = new BigMapCascadeMatcher(new TeyvatMap(), path) { LayerButtonReader = LayerButtonGeometry.Resolve })
            {
                Assert.Equal("克什尼克矿区", cascade.Match(gray, source)?.LayerName);
            }
            Assert.True(File.Exists(path));
            using var matcher = new BigMapAnchorMatcher(new TeyvatMap(), path);
            var result = matcher.Match(gray, null);
            Assert.NotNull(result);
            Assert.Contains("本地参考定位通过", matcher.LastDiagnostics);
            Assert.InRange(result.Viewport.Width, 4690, 4780);
            var center = new Point2f((float)(result.Viewport.X + result.Viewport.Width / 2),
                (float)(result.Viewport.Y + result.Viewport.Height / 2));
            var position = new TeyvatMap().ConvertImageCoordinatesToGenshinMapCoordinates(center);
            Assert.InRange(position!.Value.DistanceTo(new Point2f(3902, 10005)), 0, 20);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>禁止从未经确认的背景位置或 L3 自身采集参考，避免错误坐标自我强化。</summary>
    [Theory]
    [InlineData("L3-Anchors", -1)]
    [InlineData("L5-Background", -1)]
    [InlineData("L4-Background", null)]
    public void Observe_UnconfirmedPosition_DoesNotTeachAtlas(string via, int? floor)
    {
        using var source = MapMaskTestImages.Load("mine-runtime-overlay");
        using var gray = new Mat();
        Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
        using var matcher = new BigMapAnchorMatcher(new TeyvatMap());
        var result = new MapMatchResult(new Rect(22597, 1900, 4733, 2662), new Point2f(3902, 10005),
            floor, null, 1, via);
        matcher.Observe(gray, new Rect(0, 0, gray.Width, gray.Height), result);
        Assert.Null(matcher.Match(gray, null));
        Assert.DoesNotContain("本地参考定位通过", matcher.LastDiagnostics);
    }
}
