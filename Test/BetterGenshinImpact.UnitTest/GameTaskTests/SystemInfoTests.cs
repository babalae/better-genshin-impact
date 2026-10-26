using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Runtime;
using Vanara.PInvoke;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

/// <summary>
/// 验证游戏画面区域变化时分辨率派生数据能够原地同步
/// </summary>
public class SystemInfoTests
{
    /// <summary>
    /// 从 1920x1200 切换到 1920x1080 后应刷新全部分辨率派生数据，同时保留绑定时的 DPI 快照
    /// </summary>
    [Fact]
    public void CaptureAreaRect_WhenResolutionChanges_ShouldRefreshDerivedData()
    {
        var systemInfo = new SystemInfo(CreateViewport(1920, 1200, 1.25f));

        systemInfo.CaptureAreaRect = new RECT(100, 200, 2020, 1280);

        Assert.Equal(100, systemInfo.CaptureAreaRect.X);
        Assert.Equal(200, systemInfo.CaptureAreaRect.Y);
        Assert.Equal(1920, systemInfo.CaptureAreaRect.Width);
        Assert.Equal(1080, systemInfo.CaptureAreaRect.Height);
        Assert.Equal(1920, systemInfo.GameScreenSize.Width);
        Assert.Equal(1080, systemInfo.GameScreenSize.Height);
        Assert.Equal(1d, systemInfo.ScaleTo1080PRatio, 10);
        Assert.Equal(1d, systemInfo.ZoomOutMax1080PRatio, 10);
        Assert.Equal(1d, systemInfo.AssetScale, 10);
        Assert.Equal(100, systemInfo.ScaleMax1080PCaptureRect.X);
        Assert.Equal(200, systemInfo.ScaleMax1080PCaptureRect.Y);
        Assert.Equal(1920, systemInfo.ScaleMax1080PCaptureRect.Width);
        Assert.Equal(1080, systemInfo.ScaleMax1080PCaptureRect.Height);
        Assert.Equal(1.25f, systemInfo.DpiScale);
    }

    /// <summary>
    /// 不同分辨率更新后应沿用现有的素材缩放和等比归一化算法
    /// </summary>
    /// <param name="width">更新后的捕获宽度</param>
    /// <param name="height">更新后的捕获高度</param>
    /// <param name="expectedScaleTo1080P">预期缩放至 1080P 的比例</param>
    /// <param name="expectedAssetScale">预期素材缩放比例</param>
    /// <param name="expectedNormalizedWidth">预期归一化宽度</param>
    /// <param name="expectedNormalizedHeight">预期归一化高度</param>
    [Theory]
    [InlineData(1280, 720, 2d / 3d, 2d / 3d, 1280, 720)]
    [InlineData(2560, 1600, 4d / 3d, 1d, 1920, 1200)]
    public void CaptureAreaRect_WhenSizeChanges_ShouldKeepExistingScaleRules(
        int width,
        int height,
        double expectedScaleTo1080P,
        double expectedAssetScale,
        int expectedNormalizedWidth,
        int expectedNormalizedHeight)
    {
        var systemInfo = new SystemInfo(CreateViewport(1920, 1080));

        systemInfo.CaptureAreaRect = new RECT(0, 0, width, height);

        Assert.Equal(expectedScaleTo1080P, systemInfo.ScaleTo1080PRatio, 10);
        Assert.Equal(expectedAssetScale, systemInfo.AssetScale, 10);
        Assert.Equal(expectedAssetScale, systemInfo.ZoomOutMax1080PRatio, 10);
        Assert.Equal(expectedNormalizedWidth, systemInfo.ScaleMax1080PCaptureRect.Width);
        Assert.Equal(expectedNormalizedHeight, systemInfo.ScaleMax1080PCaptureRect.Height);
    }

    /// <summary>
    /// 按指定宽高创建测试使用的游戏画面区域
    /// </summary>
    /// <param name="width">画面宽度</param>
    /// <param name="height">画面高度</param>
    /// <param name="dpiScale">绑定时的 DPI 缩放比例</param>
    /// <returns>测试用游戏画面区域</returns>
    private static GameViewport CreateViewport(int width, int height, float dpiScale = 1f)
    {
        return new GameViewport(new RECT(0, 0, width, height), dpiScale);
    }
}
