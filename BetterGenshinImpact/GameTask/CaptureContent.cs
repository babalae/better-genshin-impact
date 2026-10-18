using BetterGenshinImpact.GameTask.Model;
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

    /// <summary>
    /// 从一帧截图构建区域树：以桌面为根派生出游戏捕获区域，大于 1080P 时再缩放到 1080P
    /// </summary>
    /// <param name="image">截图帧，所有权交给区域树</param>
    /// <param name="frameIndex">帧序号</param>
    /// <param name="interval">调度间隔（毫秒）</param>
    /// <param name="systemInfo">截图所属运行环境的画面信息，提供区域树根与捕获区域位置</param>
    public CaptureContent(Mat image, int frameIndex, double interval, ISystemInfo systemInfo)
    {
        FrameIndex = frameIndex;
        TimerInterval = interval;

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
