using System;
using System.Diagnostics;
using System.Drawing;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 捕获的内容
/// 以及一些多个trigger会用到的内容
/// </summary>
public class CaptureContent : IDisposable
{
    public static readonly int MaxFrameIndexSecond = 60;
    public Bitmap SrcBitmap { get; }
    public int FrameIndex { get; }
    public double TimerInterval { get; }

    public int FrameRate => (int)(1000 / TimerInterval);

    public RectArea CaptureRectArea { get; private set; }

    public TaskTriggerDispatcher Dispatcher { get; private set; }

    public CaptureContent(Bitmap srcBitmap, int frameIndex, double interval, TaskTriggerDispatcher dispatcher)
    {
        SrcBitmap = srcBitmap;
        FrameIndex = frameIndex;
        TimerInterval = interval;
        var systemInfo = TaskContext.Instance().SystemInfo;
        CaptureRectArea = new RectArea(srcBitmap, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y, systemInfo.DesktopRectArea);
        Dispatcher = dispatcher;
    }

    /// <summary>
    /// 达到了什么时间间隔
    /// 最大MaxFrameIndexSecond秒
    ///
    /// 这代码有bug
    /// 这代码有bug
    /// 这代码有bug
    ///
    /// 不用了
    /// </summary>
    /// <param name="interval"></param>
    /// <returns></returns>
    [Obsolete]
    public bool IsReachInterval(TimeSpan interval)
    {
        if (interval.TotalSeconds > MaxFrameIndexSecond)
        {
            throw new ArgumentException($"时间间隔不能超过{MaxFrameIndexSecond}s");
        }
        //Debug.WriteLine($"{FrameIndex}%{FrameRate * interval.TotalSeconds}={FrameIndex % (FrameRate * interval.TotalSeconds)}");
        return FrameIndex % (FrameRate * interval.TotalSeconds) == 0;
    }

    public void Dispose()
    {
        CaptureRectArea.Dispose();
        SrcBitmap.Dispose();
    }
}