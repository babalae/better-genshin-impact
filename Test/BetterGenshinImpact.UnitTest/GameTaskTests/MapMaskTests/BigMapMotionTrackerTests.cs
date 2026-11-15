using System.Diagnostics;
using BetterGenshinImpact.GameTask.MapMask;
using OpenCvSharp;
using Xunit.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>以真实地图构造已知平移和缩放，逐帧校验跟踪视口，不操作游戏。</summary>
public class BigMapMotionTrackerTests(ITestOutputHelper output)
{
    /// <summary>记录整段连续跟踪的实测耗时，不将单个算法耗时当作显示延迟。</summary>
    private readonly ITestOutputHelper _output = output;

    /// <summary>加载灰度地图供光流使用。</summary>
    private static Mat Gray(string name)
    {
        using var color = MapMaskTestImages.Load(name);
        var gray = new Mat();
        Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    /// <summary>变换地形后恢复固定 UI，避免测试依赖界面按钮跟着地图一起移动。</summary>
    private static Mat MoveMap(Mat image, double scale, double x, double y)
    {
        using var transform = new Mat(2, 3, MatType.CV_64FC1, Scalar.All(0));
        transform.Set(0, 0, scale);
        transform.Set(1, 1, scale);
        transform.Set(0, 2, x);
        transform.Set(1, 2, y);
        var moved = new Mat();
        Cv2.WarpAffine(image, moved, transform, image.Size(), borderMode: BorderTypes.Replicate);
        var regions = new[]
        {
            new Rect(0, 0, image.Width / 6, image.Height),
            new Rect(image.Width * 17 / 20, 0, image.Width - image.Width * 17 / 20, image.Height),
            new Rect(0, 0, image.Width, image.Height / 8),
            new Rect(0, image.Height * 7 / 8, image.Width, image.Height - image.Height * 7 / 8)
        };
        foreach (var region in regions)
        {
            using var originalUi = new Mat(image, region);
            using var movedUi = new Mat(moved, region);
            originalUi.CopyTo(movedUi);
        }
        return moved;
    }

    /// <summary>连续移动过程中每帧必须更新，包含真实 4K 遮挡图和右侧详情面板。</summary>
    [Theory]
    [InlineData("city-b1", false)]
    [InlineData("city-b1", true)]
    [InlineData("mine-runtime-overlay-4k", false)]
    [InlineData("city-waypoint-popup", false)]
    public void TryTrack_ContinuousMotion_UpdatesEveryFrame(string name, bool zoom)
    {
        using var tracker = new BigMapMotionTracker();
        using var image = Gray(name);
        var original = new Rect2d(1000, 2000, 2000, 2000d * image.Height / image.Width);
        Assert.False(tracker.TryTrack(image, 1, out _));
        Assert.True(tracker.SetReference(image, original, 1));
        var trackingTime = TimeSpan.Zero;
        for (var frame = 1; frame <= 16; frame++)
        {
            var scale = zoom ? 1 + frame * 0.01 : 1;
            var dx = frame * image.Width / 120d;
            var dy = -frame * image.Height / 150d;
            using var moved = MoveMap(image, scale, dx, dy);
            var timer = Stopwatch.StartNew();
            Assert.True(tracker.TryTrack(moved, frame + 1, out var view), $"{name} 第 {frame} 帧跟踪失败");
            trackingTime += timer.Elapsed;
            Assert.InRange(Math.Abs(view.X - (original.X - dx / scale * original.Width / image.Width)), 0, 3);
            Assert.InRange(Math.Abs(view.Y - (original.Y - dy / scale * original.Height / image.Height)), 0, 3);
            Assert.InRange(Math.Abs(view.Width - original.Width / scale), 0, 3);
        }
        _output.WriteLine($"{name}: 16 帧累计 {trackingTime.TotalMilliseconds:F1}ms，均值 {trackingTime.TotalMilliseconds / 16:F1}ms");
    }

    /// <summary>跟踪丢失或切图后必须等待新的绝对定位，旧修订不能复活过期视口。</summary>
    [Fact]
    public void TryTrack_UnrelatedImageOrReset_RequiresNewAbsolutePosition()
    {
        using var tracker = new BigMapMotionTracker();
        using var image = Gray("city-b1");
        using var unrelated = Gray("inazuma-surface");
        var viewport = new Rect2d(100, 200, 960, 540);
        Assert.False(tracker.TryTrack(image, 1, out _));
        Assert.True(tracker.SetReference(image, viewport, 1));
        Assert.False(tracker.TryTrack(unrelated, 2, out _));
        Assert.False(tracker.SetReference(image, viewport, 1));
        Assert.True(tracker.SetReference(unrelated, viewport, 2));
        Assert.True(tracker.TryTrack(unrelated, 2, out _));
        tracker.Reset();
        Assert.False(tracker.TryTrack(image, 3, out _));
        Assert.False(tracker.SetReference(unrelated, viewport, 2));
    }
}
