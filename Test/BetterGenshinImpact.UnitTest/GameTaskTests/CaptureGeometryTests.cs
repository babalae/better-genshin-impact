using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.UnitTest.GameTaskTests.AutoFishingTests;
using System;
using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class CaptureGeometryTests
{
    [Theory]
    [InlineData(1920, 1080, 0, 0, 1920, 1080)]
    [InlineData(2560, 1600, 0, 80, 2560, 1440)]
    [InlineData(1280, 800, 0, 40, 1280, 720)]
    [InlineData(3440, 1440, 440, 0, 2560, 1440)]
    public void FromRawCaptureRect_CalculatesCentered16x9ContentSpace(
        int rawWidth,
        int rawHeight,
        int expectedX,
        int expectedY,
        int expectedWidth,
        int expectedHeight)
    {
        var geometry = CaptureGeometry.FromRawCaptureRect(new RECT(0, 0, rawWidth, rawHeight));

        Assert.Equal(new Rect(0, 0, rawWidth, rawHeight), geometry.CaptureSpace);
        Assert.Equal(new Rect(expectedX, expectedY, expectedWidth, expectedHeight), geometry.ContentSpace);
        Assert.Equal(new RECT(expectedX, expectedY, expectedX + expectedWidth, expectedY + expectedHeight), geometry.ContentRect);
    }

    [Fact]
    public void ScaledContentRegion_Converts1080PPointBackToDesktopPoint()
    {
        var rawMat = new Mat(1600, 2560, MatType.CV_8UC3);
        var desktopRegion = new DesktopRegion(2560, 1600, new FakeMouseSimulator());
        using var rawRegion = desktopRegion.Derive(rawMat, 0, 0);
        using var contentRegion = rawRegion.DeriveCrop(new Rect(0, 80, 2560, 1440));
        using var scaledRegion = contentRegion.DeriveTo1080P();

        var (x, y) = scaledRegion.ConvertPositionToDesktopRegion(960, 540);

        Assert.Equal(1280, x);
        Assert.Equal(800, y);
    }

    [Fact]
    public void CheckGameResolution_UsesContentSizeWhenTaskContextInitialized()
    {
        var systemInfo = new FakeSystemInfo(new RECT(0, 0, 2560, 1600), 1);

        RunWithInitializedSystemInfo(systemInfo, () => AssertUtils.CheckGameResolution("自动音游"));

        Assert.Equal(new RECT(0, 0, 2560, 1440), systemInfo.GameScreenSize);
    }

    [Fact]
    public void CheckGameResolution_AllowsNative16x9Capture()
    {
        var systemInfo = new FakeSystemInfo(new RECT(0, 0, 1920, 1080), 1);

        RunWithInitializedSystemInfo(systemInfo, () => AssertUtils.CheckGameResolution("自动音游"));
    }

    [Fact]
    public void IsGameResolution16X9_ReturnsFalseWhenDerivedContentSizeIsNot16x9()
    {
        var systemInfo = new FakeSystemInfo(new RECT(0, 0, 1000, 1000), 1);

        Assert.False(AssertUtils.IsGameResolution16X9(systemInfo.GameScreenSize));
    }

    [Fact]
    public void ScaledContentRegion_Converts1080PMusicPointBackToRawGameCapturePoint()
    {
        var rawMat = new Mat(1600, 2560, MatType.CV_8UC3);
        var desktopRegion = new DesktopRegion(2560, 1600, new FakeMouseSimulator());
        using var rawRegion = desktopRegion.Derive(rawMat, 0, 0);
        using var contentRegion = rawRegion.DeriveCrop(new Rect(0, 80, 2560, 1440));
        using var scaledRegion = contentRegion.DeriveTo1080P();

        var (x, y) = scaledRegion.ConvertPositionToGameCaptureRegion(960, 921);

        Assert.Equal(1280, x);
        Assert.Equal(1308, y);
    }

    private static void RunWithInitializedSystemInfo(ISystemInfo systemInfo, Action action)
    {
        var taskContext = TaskContext.Instance();
        var originalIsInitialized = taskContext.IsInitialized;
        var originalGameHandle = taskContext.GameHandle;
        var originalSystemInfo = taskContext.SystemInfo;

        try
        {
            taskContext.IsInitialized = true;
            taskContext.GameHandle = IntPtr.Zero;
            taskContext.SystemInfo = systemInfo;
            action();
        }
        finally
        {
            taskContext.IsInitialized = originalIsInitialized;
            taskContext.GameHandle = originalGameHandle;
            taskContext.SystemInfo = originalSystemInfo;
        }
    }
}
