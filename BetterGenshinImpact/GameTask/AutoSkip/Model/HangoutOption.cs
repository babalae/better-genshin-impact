using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
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

    /// <summary>
    /// 根据选项图标位置初始化邀约选项及其文字区域
    /// </summary>
    /// <param name="runtime">识别所在的运行环境，提供捕获区域大小与素材缩放比例</param>
    /// <param name="iconRect">选项图标区域</param>
    /// <param name="selected">选项是否已被选择过</param>
    public HangoutOption(GameRuntime runtime, Region iconRect, bool selected)
    {
        IconRect = iconRect;
        IsSelected = selected;

        // 选项文字所在区域初始化
        // 选项图标往上下区域扩展 2/3
        Rect r = default;
        var captureArea = runtime.SystemInfo.ScaleMax1080PCaptureRect;
        var assetScale = runtime.SystemInfo.AssetScale;
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
            r = default;
        }

        if (r != default)
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

    public void Move()
    {
        IconRect.Move();
    }

    public void Click()
    {
        IconRect.Click();
    }

    public void BackgroundClick()
    {
        IconRect.BackgroundClick();
    }

    public void Dispose()
    {
        IconRect.Dispose();
        TextRect?.Dispose();
    }
}
