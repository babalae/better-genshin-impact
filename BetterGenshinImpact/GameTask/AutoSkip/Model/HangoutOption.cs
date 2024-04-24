using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoSkip.Model;

public class HangoutOption
{
    public Rect IconRect { get; set; }

    public Rect TextRect { get; set; }

    public bool IsSelected { get; set; }

    public string OptionTextSrc { get; set; } = "";

    public HangoutOption(Rect iconRect, bool selected)
    {
        IconRect = iconRect;
        IsSelected = selected;

        // 选项文字所在区域初始化
        // 选项图标往上下区域扩展 2/3
        var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        if (IconRect.Left > captureArea.Width / 2)
        {
            // 右边的选项
            TextRect = new Rect(IconRect.Right, IconRect.Top - IconRect.Height * 2 / 3, captureArea.Width - IconRect.Right - (int)(10 * assetScale), IconRect.Height + IconRect.Height * 4 / 3);
        }
        else if (IconRect.Right < captureArea.Width / 2)
        {
            // 左边的选项
            TextRect = new Rect((int)(10 * assetScale), IconRect.Top - IconRect.Height * 2 / 3, IconRect.Left - (int)(10 * assetScale), IconRect.Height + IconRect.Height * 4 / 3);
        }
        else
        {
            TaskControl.Logger.LogError("自动邀约：识别到错误位置的选项图标 {Rect}", IconRect);
        }

        if (TextRect.Width < captureArea.Width/8)
        {
            TaskControl.Logger.LogError("自动邀约：选项文字区域过小 {Rect}", TextRect);
            TextRect = Rect.Empty;
        }
    }

    public void Click(ClickOffset clickOffset)
    {
        var x = IconRect.X + IconRect.Width / 2;
        var y = IconRect.Y + IconRect.Height / 2;
        clickOffset.Click(x, y);
    }
}