using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.AutoSkip.Model;

public class HangoutOption : IDisposable
{
    public Region IconRect { get; set; }

    public ImageRegion? TextRect { get; set; }

    public bool IsSelected { get; set; }

    public string OptionTextSrc { get; set; } = "";

    public HangoutOption(Region iconRect, bool selected)
    {
        IconRect = iconRect;
        IsSelected = selected;

        // 选项文字所在区域初始化
        // 选项图标往上下区域扩展 2/3
        var r = Rect.Empty;
        var captureArea = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        if (IconRect.Left > captureArea.Width / 2)
        {
            // 右边的选项
            r = new Rect(IconRect.Right, IconRect.Top - IconRect.Height * 2 / 3, captureArea.Width - IconRect.Right - (int)(10 * assetScale), IconRect.Height + IconRect.Height * 4 / 3);
        }
        else if (IconRect.Right < captureArea.Width / 2)
        {
            // 左边的选项
            r = new Rect((int)(10 * assetScale), IconRect.Top - IconRect.Height * 2 / 3, IconRect.Left - (int)(10 * assetScale), IconRect.Height + IconRect.Height * 4 / 3);
        }
        else
        {
            TaskControl.Logger.LogError("自动邀约：识别到错误位置的选项图标 {Rect}", IconRect);
        }

        if (r.Width < captureArea.Width / 8)
        {
            TaskControl.Logger.LogError("自动邀约：选项文字区域过小 {Rect}", TextRect);
            r = Rect.Empty;
        }

        if (r != Rect.Empty)
        {
            if (iconRect.Prev is ImageRegion prev)
            {
                TextRect = prev.DeriveCrop(r);
            }
            else
            {
                throw new Exception("HangoutOption: IconRect.Prev is not ImageRegion");
            }
        }
    }

    public void Click()
    {
        IconRect.Click();
    }

    public void Dispose()
    {
        IconRect.Dispose();
        TextRect?.Dispose();
    }
}
