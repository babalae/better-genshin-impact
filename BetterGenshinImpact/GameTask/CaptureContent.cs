using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Generic;

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
    private readonly List<ImageRegion> _ownedRegions = [];

    public GameUiCategory CurrentGameUiCategory;

    public CaptureContent(Mat image, int frameIndex, double interval)
    {
        FrameIndex = frameIndex;
        TimerInterval = interval;
        var systemInfo = TaskContext.Instance().SystemInfo;
        var geometry = systemInfo.CaptureGeometry;

        var rawCaptureRegion = systemInfo.DesktopRectArea.Derive(image, geometry.RawCaptureRect.X, geometry.RawCaptureRect.Y);
        AddOwnedRegion(rawCaptureRegion);

        ImageRegion contentRegion = rawCaptureRegion;
        if (!geometry.ContentSpace.Equals(geometry.CaptureSpace))
        {
            contentRegion = rawCaptureRegion.DeriveCrop(geometry.ContentSpace);
            AddOwnedRegion(contentRegion);
        }

        CaptureRectArea = contentRegion.DeriveTo1080P();
        AddOwnedRegion(CaptureRectArea);
    }

    /// <summary>
    /// 用于兼容新的 ImageRegion
    /// </summary>
    /// <param name="ra"></param>
    public CaptureContent(ImageRegion ra)
    {
        CaptureRectArea = ra;
        AddOwnedRegion(ra);
    }

    public void Dispose()
    {
        for (var i = _ownedRegions.Count - 1; i >= 0; i--)
        {
            _ownedRegions[i].Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void AddOwnedRegion(ImageRegion region)
    {
        if (!_ownedRegions.Contains(region))
        {
            _ownedRegions.Add(region);
        }
    }
}
