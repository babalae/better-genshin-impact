using BetterGenshinImpact.GameTask.MapMask;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>地图变化识别应对实际平移、缩放、面板变化敏感，稳定帧及小图标动画不重启计算。</summary>
public class BigMapFrameTrackerTests
{
    /// <summary>加载真实灰度地图，不持有调用方彩色图像。</summary>
    private static Mat LoadGray(string name)
    {
        using var color = MapMaskTestImages.Load(name);
        var gray = new Mat();
        Cv2.CvtColor(color, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    /// <summary>首帧、后续平移和面板切换必须产生变化，重复截图不能重复取消。</summary>
    [Theory]
    [InlineData("city-b1", "city-b1-pan")]
    [InlineData("city-b1-close-new", "city-b1-mid-new")]
    [InlineData("city-b1", "city-waypoint-popup")]
    public void HasChanged_RealMapChanges_InvalidatesOnce(string first, string next)
    {
        using var tracker = new BigMapFrameTracker();
        using var image = LoadGray(first);
        using var changed = LoadGray(next);
        Assert.True(tracker.HasChanged(image));
        Assert.False(tracker.HasChanged(image));
        Assert.True(tracker.HasChanged(changed));
        Assert.False(tracker.HasChanged(changed));
        tracker.Reset();
        Assert.True(tracker.HasChanged(changed));
    }

    /// <summary>日志区及一个小图标变化不能造成永久取消，缓慢连续平移最终必须被检测。</summary>
    [Fact]
    public void HasChanged_AnimationsIgnoredButSlowPanAccumulates()
    {
        using var tracker = new BigMapFrameTracker();
        using var image = LoadGray("city-b1");
        Assert.True(tracker.HasChanged(image));
        using var animation = image.Clone();
        Cv2.Rectangle(animation, new Rect(0, 480, 400, 60), Scalar.White, -1);
        Cv2.Circle(animation, new Point(480, 270), 3, Scalar.White, -1);
        Assert.False(tracker.HasChanged(animation));
        var detected = false;
        using var transform = new Mat(2, 3, MatType.CV_64FC1, Scalar.All(0));
        transform.Set(0, 0, 1d);
        transform.Set(1, 1, 1d);
        using var moved = new Mat();
        for (var shift = 1; shift <= 20 && !detected; shift++)
        {
            transform.Set(0, 2, (double)shift);
            Cv2.WarpAffine(image, moved, transform, image.Size(), borderMode: BorderTypes.Replicate);
            detected = tracker.HasChanged(moved);
        }
        Assert.True(detected);
    }
}
