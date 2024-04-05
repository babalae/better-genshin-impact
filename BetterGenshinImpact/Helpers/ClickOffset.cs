using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers.Extensions;
using System;

namespace BetterGenshinImpact.Helpers;

public class ClickOffset
{
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public double AssetScale { get; set; }

    public ClickOffset()
    {
        if (!TaskContext.Instance().IsInitialized)
        {
            throw new Exception("请先启动");
        }
        var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        OffsetX = captureArea.X;
        OffsetY = captureArea.Y;
        AssetScale = assetScale;
    }

    public ClickOffset(int offsetX, int offsetY, double assetScale)
    {
        OffsetX = offsetX;
        OffsetY = offsetY;
        AssetScale = assetScale;
    }

    public void Click(int x, int y)
    {
        ClickExtension.Click(OffsetX + (int)(x * AssetScale), OffsetY + (int)(y * AssetScale));
    }

    public void ClickWithoutScale(int x, int y)
    {
        ClickExtension.Click(OffsetX + x, OffsetY + y);
    }

    public void ClickWithoutScale(double x, double y)
    {
        ClickExtension.Click(OffsetX + x, OffsetY + y);
    }

    public void Move(int x, int y)
    {
        ClickExtension.Move(OffsetX + (int)(x * AssetScale), OffsetY + (int)(y * AssetScale));
    }
}
