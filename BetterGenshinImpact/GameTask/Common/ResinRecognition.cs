using System;
using System.Globalization;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// 大地图顶栏原粹树脂识别。图标模板匹配与 OCR 区域参考 AutoBossTask 的实现。
/// </summary>
public static class ResinRecognition
{
    /// <summary>
    /// 在大地图界面顶栏识别原粹树脂当前值与上限。
    /// 不点击图标，直接解析顶栏 "当前/上限" 文本。
    /// </summary>
    /// <param name="capture">大地图界面的截图区域</param>
    /// <param name="resinIconRect">输出：树脂图标在截图中的矩形（绝对坐标），可用于裁剪截图条；识别失败时为 default</param>
    /// <returns>(当前值, 上限)；识别失败返回 null</returns>
    public static (int Current, int Max)? RecognizeInBigMapTopBar(ImageRegion capture, out Rect resinIconRect)
    {
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        using var iconSearchRegion = capture.DeriveCrop(new Rect(
            (int)(1200 * assetScale), (int)(25 * assetScale),
            (int)(580 * assetScale), (int)(50 * assetScale)));
        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var iconRa = iconSearchRegion.Find(RecognitionAssets.Get("AutoBoss", "OriginalResinTopIcon", captureRect.Width, captureRect.Height));
        if (iconRa.IsEmpty())
        {
            resinIconRect = default;
            return null;
        }

        resinIconRect = new Rect(
            iconSearchRegion.X + iconRa.Left,
            iconSearchRegion.Y + iconRa.Top,
            iconRa.Width,
            iconRa.Height);

        var countRect = new Rect(
            resinIconRect.Right + (int)(25 * assetScale),
            (int)(37 * assetScale),
            (int)(120 * assetScale),
            (int)(24 * assetScale));
        using var countRegion = capture.DeriveCrop(countRect);
        var countText = OcrFactory.Paddle.OcrWithoutDetector(countRegion.SrcMat);

        // 顶栏文本为 "当前/上限"，上限固定为三位数(200)；斜杠可能被 OCR 认成 7 或 1，
        // 因此删除全部非数字后按"后三位为上限、其余为当前值"切分
        var digits = Regex.Replace(StringUtils.ConvertFullWidthNumToHalfWidth(countText), @"\D", "");
        if (digits.Length < 4)
        {
            return null;
        }

        if (!int.TryParse(digits[..^3], NumberStyles.None, CultureInfo.InvariantCulture, out var current)
            || !int.TryParse(digits[^3..], NumberStyles.None, CultureInfo.InvariantCulture, out var max)
            || current < 0 || max <= 0 || current > max)
        {
            return null;
        }

        return (current, max);
    }
}
