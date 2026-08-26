using System;
using System.Globalization;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common;

/// <summary>
/// 大地图顶栏树脂识别结果。
/// </summary>
/// <param name="Current">当前原粹树脂数量</param>
/// <param name="Max">原粹树脂上限</param>
/// <param name="Condensed">浓缩树脂数量；图标或数量识别失败时为空</param>
/// <param name="OriginalIconRect">原粹树脂图标矩形</param>
/// <param name="CondensedIconRect">浓缩树脂图标矩形；图标未匹配时为空</param>
public readonly record struct ResinRecognitionResult(
    int Current,
    int Max,
    int? Condensed,
    Rect OriginalIconRect,
    Rect? CondensedIconRect);

/// <summary>
/// 大地图顶栏原粹树脂与浓缩树脂识别。
/// </summary>
public static class ResinRecognition
{
    private const int TextThreshold = 180;

    /// <summary>
    /// 在大地图界面顶栏识别原粹树脂当前值与上限，并以原粹树脂图标为锚点识别左侧的浓缩树脂数量。
    /// </summary>
    /// <param name="capture">大地图界面的截图区域</param>
    /// <returns>树脂识别结果；原粹树脂识别失败时返回 null</returns>
    public static ResinRecognitionResult? RecognizeInBigMapTopBar(ImageRegion capture)
    {
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;

        using var iconSearchRegion = capture.DeriveCrop(new Rect(
            (int)(1200 * assetScale), (int)(25 * assetScale),
            (int)(580 * assetScale), (int)(50 * assetScale)));
        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var iconRa = iconSearchRegion.Find(RecognitionAssets.Get("AutoBoss", "OriginalResinTopIcon", captureRect.Width, captureRect.Height));
        if (iconRa.IsEmpty())
        {
            return null;
        }

        var resinIconRect = new Rect(
            iconSearchRegion.X + iconRa.Left,
            iconSearchRegion.Y + iconRa.Top,
            iconRa.Width,
            iconRa.Height);
        var (condensedResin, condensedIconRect) = RecognizeCondensedResin(
            capture, resinIconRect, assetScale);

        var countRect = new Rect(
            resinIconRect.Right + (int)(25 * assetScale),
            (int)(33 * assetScale),
            (int)(105 * assetScale),
            (int)(25 * assetScale));
        using var countRegion = capture.DeriveCrop(countRect);

        using var threshold = countRegion.CacheGreyMat.Threshold(
            TextThreshold, 255, ThresholdTypes.Binary);
        using var inverted = new Mat();
        Cv2.BitwiseNot(threshold, inverted);

        var countText = OcrFactory.Paddle.OcrWithoutDetector(inverted);

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

        return new ResinRecognitionResult(
            current, max, condensedResin, resinIconRect, condensedIconRect);
    }

    private static (int? Count, Rect? IconRect) RecognizeCondensedResin(
        ImageRegion capture,
        Rect originalResinIconRect,
        double assetScale)
    {
        // 复用秘境树脂识别中的相对关系：浓缩树脂图标位于原粹树脂图标左侧约 90～180 像素。
        var desiredLeft = originalResinIconRect.Left - (int)(180 * assetScale);
        var desiredTop = originalResinIconRect.Top - (int)(15 * assetScale);
        var searchLeft = Math.Max(0, desiredLeft);
        var searchTop = Math.Max(0, desiredTop);
        var searchRight = Math.Min(capture.Width, originalResinIconRect.Left - (int)(90 * assetScale));
        var searchBottom = Math.Min(capture.Height, desiredTop + (int)(50 * assetScale));
        if (searchRight <= searchLeft || searchBottom <= searchTop)
        {
            return (null, null);
        }

        using var searchRegion = capture.DeriveCrop(new Rect(
            searchLeft, searchTop, searchRight - searchLeft, searchBottom - searchTop));

        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var iconRa = searchRegion.Find(RecognitionAssets.Get(
            "AutoFight", "CondensedResinTopIcon", captureRect.Width, captureRect.Height));
        if (iconRa.IsEmpty())
        {
            TaskControl.Logger.LogDebug("大地图顶栏未匹配到浓缩树脂图标");
            return (null, null);
        }

        var iconRect = new Rect(
            searchRegion.X + iconRa.Left,
            searchRegion.Y + iconRa.Top,
            iconRa.Width,
            iconRa.Height);

        var countLeft = iconRect.Right + (int)(20 * assetScale);
        var countRight = Math.Min(capture.Width, countLeft + (int)(30 * assetScale));
        var countBottom = Math.Min(capture.Height, iconRect.Bottom);
        if (countRight <= countLeft || countBottom <= iconRect.Top)
        {
            return (null, iconRect);
        }

        using var countRegion = capture.DeriveCrop(new Rect(
            countLeft, iconRect.Top, countRight - countLeft, countBottom - iconRect.Top));

        using var threshold = countRegion.CacheGreyMat.Threshold(
            TextThreshold, 255, ThresholdTypes.Binary);
        using var inverted = new Mat();
        Cv2.BitwiseNot(threshold, inverted);

        var countText = OcrFactory.Paddle.OcrWithoutDetector(inverted);
        var digits = Regex.Replace(StringUtils.ConvertFullWidthNumToHalfWidth(countText), @"\D", "");
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count is < 0 or > 5)
        {
            TaskControl.Logger.LogDebug("浓缩树脂数量 OCR 失败：{Text}", countText);
            return (null, iconRect);
        }

        TaskControl.Logger.LogDebug("浓缩树脂数量 OCR：{Text} -> {Count}", countText, count);
        return (count, iconRect);
    }
}
