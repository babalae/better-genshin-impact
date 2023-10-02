using System;
using System.Drawing;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 捕获的内容
/// 以及一些多个trigger会用到的内容
/// </summary>
public class CaptureContent
{
    public static readonly int MaxFrameIndexSecond = 60;
    public Bitmap SrcBitmap { get; }
    public int FrameIndex { get; }
    public double TimerInterval { get; }

    public int FrameRate => (int)(1000 /  TimerInterval);

    public RectArea CaptureRectArea { get; private set; }

    public CaptureContent(Bitmap srcBitmap, int frameIndex, double interval)
    {
        SrcBitmap = srcBitmap;
        FrameIndex = frameIndex;
        TimerInterval = interval;
        var systemInfo = TaskContext.Instance().SystemInfo;
        CaptureRectArea = new RectArea(srcBitmap, systemInfo.GameWindowRect.X, systemInfo.GameWindowRect.Y, systemInfo.DesktopRectArea);
    }

    private Mat? _srcMat;

    public Mat SrcMat
    {
        get
        {
            _srcMat ??= SrcBitmap.ToMat();
            return _srcMat;
        }
    }

    private Mat? _srcGreyMat;

    public Mat SrcGreyMat
    {
        get
        {
            _srcGreyMat ??= new Mat();
            Cv2.CvtColor(SrcMat, _srcGreyMat, ColorConversionCodes.BGR2GRAY);
            return _srcGreyMat;
        }
    }

    /// <summary>
    /// 达到了什么时间间隔
    /// 最大MaxFrameIndexSecond秒
    /// </summary>
    /// <param name="interval"></param>
    /// <returns></returns>
    public bool IsReachInterval(TimeSpan interval)
    {
        if (interval.TotalSeconds > MaxFrameIndexSecond)
        {
            throw new ArgumentException($"时间间隔不能超过{MaxFrameIndexSecond}s");
        }

        return interval.TotalMilliseconds >= FrameIndex * TimerInterval && interval.TotalMilliseconds < (FrameIndex + 1) * TimerInterval;
    }
}