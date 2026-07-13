using BetterGenshinImpact.GameTask.Model.Area;
using System;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 捕获的内容
/// 以及一些多个trigger会用到的内容
/// </summary>
public class CaptureContent : IDisposable
{
    public static readonly int MaxFrameIndexSecond = 60;
    public int FrameIndex { get; }
    public double TimerInterval { get; }

    public int FrameRate => (int)(1000 / TimerInterval);

    public ImageRegion CaptureRectArea { get; }
    
    public GameUiCategory CurrentGameUiCategory;

    public CaptureContent(Mat image, int frameIndex, double interval)
    {
        FrameIndex = frameIndex;
        TimerInterval = interval;
        var systemInfo = TaskContext.Instance().SystemInfo;

        var gameCaptureRegion = systemInfo.DesktopRectArea.Derive(image, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y);
        CaptureRectArea = gameCaptureRegion.DeriveTo1080P();
    }

    /// <summary>
    /// 用于兼容新的 ImageRegion
    /// </summary>
    /// <param name="ra"></param>
    public CaptureContent(ImageRegion ra)
    {
        CaptureRectArea = ra;
    }

    public void Dispose()
    {
        CaptureRectArea.Dispose();
        GC.SuppressFinalize(this);
    }
}
