using BetterGenshinImpact.GameTask.Model.Area;
using System;
using System.Drawing;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 捕获的内容
/// 以及一些多个trigger会用到的内容
/// </summary>
public class CaptureContent : IDisposable
{
    public static readonly int MaxFrameIndexSecond = 60;
    private Bitmap? SrcBitmap { get; }
    public int FrameIndex { get; }
    public double TimerInterval { get; }

    public int FrameRate => (int)(1000 / TimerInterval);

    public ImageRegion CaptureRectArea { get; private set; }

    public CaptureContent(Bitmap srcBitmap, int frameIndex, double interval)
    {
        SrcBitmap = srcBitmap;
        FrameIndex = frameIndex;
        TimerInterval = interval;
        var systemInfo = TaskContext.Instance().SystemInfo;

        var gameCaptureRegion = systemInfo.DesktopRectArea.Derive(srcBitmap, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y);
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
        SrcBitmap?.Dispose();
    }
}
