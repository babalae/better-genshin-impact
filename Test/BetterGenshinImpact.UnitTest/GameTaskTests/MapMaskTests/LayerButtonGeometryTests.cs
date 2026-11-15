using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.MapMask;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>真实地图按钮回归：覆盖地表多层区域、切层、缩放及生产模板通道。</summary>
public class LayerButtonGeometryTests
{
    /// <summary>截图及缩放组合，文件是仓库内固定样本，不依赖用户捕获目录。</summary>
    public static IEnumerable<object[]> Screenshots()
    {
        var samples = new (string Name, int Depth)[]
        {
            ("city-b1", 1), ("city-surface", 0), ("city-b1-pan", 1), ("city-b2", 2),
            ("city-surface-zoom", 0), ("city-b1-east", 1), ("station-surface", 0),
            ("station-b1", 1), ("station-b2", 2), ("station-b2-zoom", 2), ("mine-b1-failure", 1),
            ("city-waypoint-popup", 1), ("city-b1-wide-new", 1), ("city-b1-close-new", 1),
            ("city-b1-mid-new", 1), ("city-b1-zoom-new", 1), ("mine-marker-popup", 1)
        };
        foreach (var (name, depth) in samples)
        {
            foreach (var width in new[] { 960, 1920, 3840 })
            {
                yield return [name, depth, width];
            }
        }
    }

    /// <summary>保留原始 4K 输入，避免仅放大小图掩盖按钮模板的真实缩放问题。</summary>
    [Fact]
    public void Resolve_OriginalFailureScreenshot_ReturnsUnderground()
    {
        using var source = MapMaskTestImages.Load("mine-b1-failure-4k");
        Assert.Equal(new LayerButtonSignal(true, 1), LayerButtonGeometry.Resolve(source));
    }

    /// <summary>调用真实生产入口，能发现生产模板已是灰度或被重复缩放的问题。</summary>
    [Theory]
    [MemberData(nameof(Screenshots))]
    public void Resolve_SelectedButton_ReturnsExpectedDepth(string name, int depth, int width)
    {
        using var source = MapMaskTestImages.Load(name);
        using var resized = new Mat();
        Cv2.Resize(source, resized, new Size(width, width * 9 / 16));
        var signal = LayerButtonGeometry.Resolve(resized);
        Assert.NotNull(signal);
        Assert.Equal(depth > 0, signal.IsUnderground);
        Assert.Equal(depth, signal.DepthFromSurface);
    }

    /// <summary>无地图按钮的纯色图不能误报地下。</summary>
    [Fact]
    public void Resolve_BlankOrEmpty_ReturnsNull()
    {
        using var image = new Mat(540, 960, MatType.CV_8UC3, Scalar.Gray);
        using var empty = new Mat();
        Assert.Null(LayerButtonGeometry.Resolve(image));
        Assert.Null(LayerButtonGeometry.Resolve(empty));
    }

    /// <summary>注入灰度地表模板时仍须与彩色截图正常配合，不应因通道不同而抛异常。</summary>
    [Fact]
    public void Resolve_GrayscaleGroundTemplate_ReturnsSelectedLayer()
    {
        using var image = MapMaskTestImages.Load("city-b1");
        var ground = RecognitionAssets.Get("QuickTeleport", "MapUndergroundToGroundButton", 1920, 1080).TemplateImageMat;
        var selected = RecognitionAssets.Get("QuickTeleport", "MapUndergroundSwitchButton", 1920, 1080).TemplateImageMat;
        Assert.NotNull(ground);
        Assert.NotNull(selected);
        using var grayGround = new Mat();
        if (ground.Channels() == 1)
        {
            ground.CopyTo(grayGround);
        }
        else
        {
            Cv2.CvtColor(ground, grayGround, ColorConversionCodes.BGR2GRAY);
        }

        Assert.Equal(new LayerButtonSignal(true, 1), LayerButtonGeometry.Resolve(image, grayGround, selected));
    }
}
