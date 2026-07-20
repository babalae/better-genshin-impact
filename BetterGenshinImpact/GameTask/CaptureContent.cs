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

    /// <summary>
    /// 是否拥有 <see cref="CaptureRectArea"/>。
    /// 由 Mat 构造时为 true（本对象负责释放）；包装已有 <see cref="ImageRegion"/> 时为 false（只借用）。
    /// </summary>
    private readonly bool _ownsCaptureRectArea;

    public GameUiCategory CurrentGameUiCategory;

    /// <summary>
    /// 由一帧截图构造，取得该帧的所有权，必须 Dispose 本对象。
    /// </summary>
    public CaptureContent(Mat image, int frameIndex, double interval)
    {
        FrameIndex = frameIndex;
        TimerInterval = interval;
        _ownsCaptureRectArea = true;
        var systemInfo = TaskContext.Instance().SystemInfo;

        var gameCaptureRegion = systemInfo.DesktopRectArea.Derive(image, systemInfo.CaptureAreaRect.X, systemInfo.CaptureAreaRect.Y);
        try
        {
            CaptureRectArea = gameCaptureRegion.DeriveTo1080P();
        }
        catch
        {
            // 构造未完成时外层 using 不会生效，必须在这里释放原始帧。
            // DeriveTo1080P() 的 wrapper 构造失败路径上 gameCaptureRegion 可能已被释放，这里依赖 Dispose 幂等。
            gameCaptureRegion.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 用于兼容新的 ImageRegion。
    /// 只借用传入区域，本对象不拥有它，Dispose 本对象不会释放它。
    /// </summary>
    /// <param name="ra"></param>
    public CaptureContent(ImageRegion ra)
    {
        CaptureRectArea = ra;
        _ownsCaptureRectArea = false;
    }

    public void Dispose()
    {
        if (_ownsCaptureRectArea)
        {
            CaptureRectArea.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
