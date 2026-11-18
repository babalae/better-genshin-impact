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
    /// 顶栏树脂图标的搜索范围（1080P 坐标）。
    /// 顶栏元素数量会随深渊 / 幻想真境剧诗等进度徽章增减而变化，整组元素以右侧关闭按钮为锚点
    /// 向左排列，因此树脂图标的位置并不固定；固定左边界在徽章较多时会直接漏掉图标。
    /// 这里改为覆盖左侧「列车」卡片之后的整条顶栏，避免再被徽章数量变化影响。
    /// </summary>
    private const int IconSearchLeft = 360;

    private const int IconSearchTop = 25;
    private const int IconSearchHeight = 50;

    /// <summary>
    /// 数量文本相对树脂图标的位置与范围（1080P 坐标）。
    /// 宽度按「200/200」三位数上限预留，避免 100+ 时把末尾的 200 截断。
    /// </summary>
    private const int CountOffsetX = 25;
    private const int CountOffsetY = 30;
    private const int CountWidth = 150;
    private const int CountHeight = 32;

    /// <summary>
    /// 「+」补充树脂按钮的搜索范围（1080P 坐标，相对数量文本左边界）。
    /// 按钮紧跟在数量文本右侧，用它做数量文本的右边界可以避免把白色圆底送进 OCR。
    /// </summary>
    private const int PlusSearchWidth = 220;
    private const int PlusSearchTop = 25;
    private const int PlusSearchHeight = 50;
    private const int CountRightPadding = 4;

    /// <summary>
    /// 在大地图界面顶栏识别原粹树脂当前值与上限，并以原粹树脂图标为锚点识别左侧的浓缩树脂数量。
    /// </summary>
    /// <param name="capture">大地图界面的截图区域</param>
    /// <returns>树脂识别结果；原粹树脂识别失败时返回 null</returns>
    public static ResinRecognitionResult? RecognizeInBigMapTopBar(ImageRegion capture)
    {
        var assetScale = capture.GetAssetScale();

        var iconSearchLeft = (int)(IconSearchLeft * assetScale);
        var iconSearchTop = (int)(IconSearchTop * assetScale);
        var iconSearchWidth = capture.Width - iconSearchLeft;
        var iconSearchHeight = (int)(IconSearchHeight * assetScale);
        if (iconSearchWidth <= 0 || iconSearchHeight <= 0)
        {
            TaskControl.Logger.LogDebug(
                "大地图顶栏树脂图标搜索区域无效：截图 {Width}x{Height}，素材缩放 {Scale}",
                capture.Width, capture.Height, assetScale);
            return null;
        }

        using var iconSearchRegion = capture.DeriveCrop(new Rect(
            iconSearchLeft, iconSearchTop, iconSearchWidth, iconSearchHeight));
        var captureRect = capture.GetScaleMax1080PFrameSize();
        var iconRa = iconSearchRegion.Find(RecognitionAssets.Get("AutoBoss", "OriginalResinTopIcon", captureRect.Width, captureRect.Height));
        if (iconRa.IsEmpty())
        {
            TaskControl.Logger.LogDebug(
                "大地图顶栏未匹配到原粹树脂图标：搜索区域 ({X},{Y},{W},{H})",
                iconSearchLeft, iconSearchTop, iconSearchWidth, iconSearchHeight);
            return null;
        }

        var resinIconRect = new Rect(
            iconSearchRegion.X + iconRa.Left,
            iconSearchRegion.Y + iconRa.Top,
            iconRa.Width,
            iconRa.Height);
        TaskControl.Logger.LogDebug(
            "大地图顶栏原粹树脂图标：({X},{Y}) 匹配度 {Score:F3}",
            resinIconRect.X, resinIconRect.Y, iconRa.MatchScore);
        var (condensedResin, condensedIconRect) = RecognizeCondensedResin(
            capture, resinIconRect, assetScale);

        var countLeft = resinIconRect.Right + (int)(CountOffsetX * assetScale);
        var countRight = Math.Min(capture.Width, countLeft + (int)(CountWidth * assetScale));

        // 「+」按钮紧跟在数量文本右侧；优先以它为右边界：
        // 既能避免把按钮的白色圆底一起送进 OCR，也能保证 100+ 的三位树脂不会被截断。
        var plusLeft = TryFindResinSupplementButtonLeft(capture, countLeft, assetScale, captureRect);
        if (plusLeft is int plusButtonLeft
            && plusButtonLeft - (int)(CountRightPadding * assetScale) > countLeft)
        {
            countRight = Math.Min(countRight, plusButtonLeft - (int)(CountRightPadding * assetScale));
        }

        if (countRight <= countLeft)
        {
            TaskControl.Logger.LogDebug(
                "大地图顶栏原粹树脂数量区域无效：图标 ({X},{Y},{W},{H})",
                resinIconRect.X, resinIconRect.Y, resinIconRect.Width, resinIconRect.Height);
            return null;
        }

        var countRect = new Rect(
            countLeft,
            (int)(CountOffsetY * assetScale),
            countRight - countLeft,
            (int)(CountHeight * assetScale));
        using var countRegion = capture.DeriveCrop(countRect);

        using var threshold = countRegion.CacheGreyMat.Threshold(
            TextThreshold, 255, ThresholdTypes.Binary);
        using var inverted = new Mat();
        Cv2.BitwiseNot(threshold, inverted);

        var countText = OcrFactory.Paddle.OcrWithoutDetector(inverted);

        // 顶栏文本为 "当前/上限"，上限固定为三位数(200)；斜杠可能被 OCR 认成 1 或 7，
        // 因此把 /、1、7 都作为分隔符处理。原粹树脂可在使用补充道具后暂时超过上限，
        // 因此不校验 current <= max。
        var match = Regex.Match(
            StringUtils.ConvertFullWidthNumToHalfWidth(countText),
            @"(\d{1,3})\s*[/17]\s*(200)");
        if (!match.Success)
        {
            TaskControl.Logger.LogDebug(
                "大地图顶栏原粹树脂数量 OCR 结果不匹配：\"{Text}\"（区域 ({X},{Y},{W},{H})）",
                countText, countRect.X, countRect.Y, countRect.Width, countRect.Height);
            return null;
        }

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var current)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var max))
        {
            return null;
        }

        return new ResinRecognitionResult(
            current, max, condensedResin, resinIconRect, condensedIconRect);
    }

    /// <summary>
    /// 在数量文本右侧查找「+」补充树脂按钮，返回其左边界；未找到时返回 null。
    /// </summary>
    private static int? TryFindResinSupplementButtonLeft(
        ImageRegion capture, int searchLeft, double assetScale, Size captureRect)
    {
        var width = Math.Min(capture.Width - searchLeft, (int)(PlusSearchWidth * assetScale));
        var height = (int)(PlusSearchHeight * assetScale);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        using var searchRegion = capture.DeriveCrop(new Rect(
            searchLeft, (int)(PlusSearchTop * assetScale), width, height));
        using var buttonRa = searchRegion.Find(RecognitionAssets.Get(
            "AutoBoss", "OpenResinSupplementPaneButton", captureRect.Width, captureRect.Height));
        return buttonRa.IsEmpty() ? null : searchRegion.X + buttonRa.Left;
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

        var captureRect = capture.GetScaleMax1080PFrameSize();
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
