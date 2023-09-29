using System;
using System.Drawing;
using BetterGenshinImpact.Core.Recognition.OpenCv;
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
    public int FrameIndex { get; private set; }
    public int FrameRate { get; private set; }

    public CaptureContent(Bitmap srcBitmap, int frameIndex, int frameRate)
    {
        SrcBitmap = srcBitmap;
        FrameIndex = frameIndex;
        FrameRate = frameRate;
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

    private Mat? _srcGreyRightBottomMat;
    /// <summary>
    /// 1/2 右 2/3 下
    /// </summary>
    public Mat SrcGreyRightBottomMat
    {
        get
        {
            _srcGreyRightBottomMat ??= CutHelper.CutRightBottom(SrcGreyMat, SrcGreyMat.Width / 2, SrcGreyMat.Height / 3 * 2);
            return _srcGreyRightBottomMat;
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

        return FrameIndex % (FrameRate * interval.TotalSeconds) == 0;
    }
}