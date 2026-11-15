using BetterGenshinImpact.GameTask.MapMask;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>L1 的真实滑块读取及校准回归，禁止把 UI 等级直接当成地图像素比例。</summary>
public class BigMapZoomTests
{
    /// <summary>不同分辨率读取的 UI 位置必须一致，覆盖地表、地下和失败原图。</summary>
    [Theory]
    [InlineData("city-surface", 506)]
    [InlineData("city-b1", 506)]
    [InlineData("city-b2", 514)]
    [InlineData("station-surface", 534)]
    [InlineData("mine-b1-failure", 548)]
    [InlineData("mine-b1-failure-4k", 548)]
    [InlineData("inazuma-zoom", 500)]
    [InlineData("inazuma-zoom-near", 468)]
    [InlineData("inazuma-zoom-far", 612)]
    public void Read_RealSlider_IsIndependentOfResolution(string name, double centerAt1080)
    {
        using var source = MapMaskTestImages.Load(name);
        foreach (var width in new[] { 960, 1920, 3840 })
        {
            using var color = new Mat();
            using var gray = new Mat();
            Cv2.Resize(source, color, new Size(width, width * 9 / 16));
            Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
            var signal = BigMapZoom.Read(gray);
            Assert.NotNull(signal);
            Assert.InRange(signal.Position, Math.Max(0, (centerAt1080 - 471) / 144), Math.Min(1, (centerAt1080 - 465) / 144));
            Assert.InRange(signal.Confidence, 0.68, 1);
        }
    }

    /// <summary>空白地图和无效输入不能提供可用于定位的缩放比例。</summary>
    [Fact]
    public void Read_NoSlider_ReturnsNull()
    {
        using var empty = new Mat();
        using var blank = new Mat(540, 960, MatType.CV_8UC1, Scalar.Gray);
        using var color = new Mat(540, 960, MatType.CV_8UC3, Scalar.Gray);
        Assert.Null(BigMapZoom.Read(empty));
        Assert.Null(BigMapZoom.Read(blank));
        Assert.Null(BigMapZoom.Read(color));
    }

    /// <summary>只有实测视口才可校准，等级和分辨率变化均须正确转换，重开后清除校准。</summary>
    [Fact]
    public void Calibration_UsesVerifiedScaleAndResetsBetweenSessions()
    {
        var calibration = new BigMapZoom();
        var first = new BigMapZoomSignal(0.3, 0.9);
        Assert.Equal(0.2, calibration.Predict(first, 0.5)!.Value.Tolerance);
        calibration.Observe(first, 4);
        Assert.Equal(8, calibration.Predict(first, 0.5)!.Value.Scale, 4);
        Assert.Equal(2, calibration.Predict(first, 2)!.Value.Scale, 4);
        Assert.Equal(8 * 3 / 2.5, calibration.Predict(new BigMapZoomSignal(0.4, 0.9), 0.5)!.Value.Scale, 4);
        Assert.Equal(8 * 3.5 / 2.5,
            calibration.Predict(new BigMapZoomSignal(0.5, 0.9), 0.5)!.Value.Scale, 4);
        calibration.Observe(null, 1000);
        calibration.Observe(first, double.NaN);
        Assert.Equal(8, calibration.Predict(first, 0.5)!.Value.Scale, 4);
        calibration.Reset();
        Assert.Equal(0.2, calibration.Predict(first, 0.5)!.Value.Tolerance);
        Assert.Null(calibration.Predict(null, 0.5));
        Assert.Null(calibration.Predict(first, 0));
    }

    /// <summary>独立地图按实测滑块范围学习，冷启动与未知缩放不能用大陆比例排除正确锚点。</summary>
    [Fact]
    public void Calibration_IndependentMap_LearnsItsOwnSliderRange()
    {
        var calibration = new BigMapZoom(useTeyvatPrior: false);
        var first = new BigMapZoomSignal(0, 1);
        var second = new BigMapZoomSignal(0.25, 1);
        Assert.Null(calibration.Predict(first, 0.5));
        calibration.Observe(first, 0.43);
        Assert.Equal(0.86, calibration.Predict(first, 0.5)!.Value.Scale, 4);
        Assert.Null(calibration.Predict(second, 0.5));
        calibration.Observe(second, 0.75);
        Assert.Equal(1.5, calibration.Predict(second, 0.5)!.Value.Scale, 4);
        Assert.Equal(2.14, calibration.Predict(new BigMapZoomSignal(0.5, 1), 0.5)!.Value.Scale, 4);
        calibration.Reset();
        Assert.Null(calibration.Predict(first, 0.5));
    }

    /// <summary>L1 冷启动范围必须覆盖真实最大放大、常用比例及最大缩小视口。</summary>
    [Theory]
    [InlineData("city-surface", 3860)]
    [InlineData("mine-b1-failure-4k", 6257)]
    [InlineData("inazuma-surface", 5759)]
    [InlineData("inazuma-zoom", 3496)]
    [InlineData("inazuma-zoom-near", 1640)]
    [InlineData("inazuma-zoom-far", 9854)]
    public void Calibration_ColdRange_ContainsActualViewportScale(string name, int viewportWidth)
    {
        using var color = MapMaskTestImages.Load(name);
        using var gray = new Mat();
        Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        var zoom = BigMapZoom.Read(gray);
        Assert.NotNull(zoom);
        var prior = new BigMapZoom().Predict(zoom, Math.Min(gray.Width / 1920d, gray.Height / 1080d));
        Assert.NotNull(prior);
        Assert.InRange(viewportWidth / (double)gray.Width,
            prior.Value.Scale * (1 - prior.Value.Tolerance), prior.Value.Scale * (1 + prior.Value.Tolerance));
    }
}
