using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.GameCapture;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoTrackPathTests;

public class CaptureColorModePropagationTests
{
    /// <summary>
    /// 验证直截图会返回产生该图像帧的实际色彩模式。
    /// </summary>
    [Fact]
    public void CaptureGameImage_ReturnsCurrentFrameColorMode()
    {
        using var capture = new StubGameCapture(CaptureColorMode.HdrToSdr);

        using var image = TaskControl.CaptureGameImage(capture, out var colorMode);

        Assert.Equal(CaptureColorMode.HdrToSdr, colorMode);
    }

    /// <summary>
    /// 验证裁剪派生区域会保留源截图帧的实际色彩模式。
    /// </summary>
    [Fact]
    public void ImageRegion_DeriveCrop_PreservesCurrentFrameColorMode()
    {
        using var region = new ImageRegion(new Mat(8, 8, MatType.CV_8UC3), 0, 0)
        {
            ColorMode = CaptureColorMode.HdrToSdr
        };

        using var crop = region.DeriveCrop(0, 0, 4, 4);

        Assert.Equal(CaptureColorMode.HdrToSdr, crop.ColorMode);
    }

    /// <summary>
    /// 验证地图候选阈值直接由当前帧色彩模式决定。
    /// </summary>
    [Theory]
    [InlineData(CaptureColorMode.Sdr, 0.8)]
    [InlineData(CaptureColorMode.HdrToSdr, 0.7)]
    public void ResolveMapChooseIconThreshold_UsesCurrentFrameColorMode(
        CaptureColorMode colorMode,
        double expected)
    {
        Assert.Equal(expected, TpTask.ResolveMapChooseIconThreshold(colorMode));
    }

    private sealed class StubGameCapture(CaptureColorMode colorMode) : IGameCapture
    {
        public bool IsCapturing => true;

        public CaptureColorMode ColorMode => colorMode;

        /// <summary>
        /// 释放测试截图器资源。
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// 启动测试截图器。
        /// </summary>
        public void Start(nint hWnd, Dictionary<string, object>? settings = null)
        {
        }

        /// <summary>
        /// 创建携带指定色彩模式的测试帧。
        /// </summary>
        public GameCaptureFrame Capture()
        {
            return new GameCaptureFrame(new Mat(8, 8, MatType.CV_8UC3), colorMode: colorMode);
        }

        /// <summary>
        /// 停止测试截图器。
        /// </summary>
        public void Stop()
        {
        }
    }
}
