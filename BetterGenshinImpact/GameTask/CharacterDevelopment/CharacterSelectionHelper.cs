using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.CharacterDevelopment;

/// <summary>
/// 一次角色选择所需的标准名称、候选名称和筛选策略。
/// </summary>
/// <remarks>
/// 旅行者会展开为空/荧两个候选；自定义显示名角色可跳过角色名 OCR 确认。
/// </remarks>
internal sealed record CharacterSelectionTarget(
    string Name,
    string[] CandidateNames,
    bool SkipElementFilter,
    string? ForcedWeaponType,
    bool SkipDisplayNameConfirmation,
    bool RecognizeElementType)
{
    public string PrimaryCandidateName => CandidateNames[0];

    public bool Matches(string? characterName) =>
        characterName != null && CandidateNames.Contains(characterName, StringComparer.Ordinal);

    public bool MatchesDisplayText(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && CandidateNames.Any(candidate => text.Contains(candidate, StringComparison.Ordinal));
}

/// <summary>
/// 角色卡片及模型输入裁剪区域，坐标均相对于角色列表 ROI。
/// </summary>
internal sealed record CharacterCardRect(Rect CardRect, Rect AvatarRect, Rect ElementRect);

/// <summary>
/// 角色列表筛选、卡片定位和头像匹配的专用实现，不与旧角色切换网格共享。
/// </summary>
internal static class CharacterSelectionHelper
{
    /// <summary>头像 embedding 余弦相似度的最低接受分数。</summary>
    internal const double MatchThreshold = 0.7;
    private const int EmptyDetectionRetryCount = 3;
    private static readonly Rect GridRoi1080 = new(40, 76, 641, 897);

    public static CharacterSelectionTarget CreateTarget(string name)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("角色名不能为空。", nameof(name));
        }

        var standardName = ToConfiguredAvatarName(normalizedName);
        if (standardName == "旅行者")
        {
            return new CharacterSelectionTarget(
                "旅行者",
                ["空", "荧"],
                true,
                "单手剑",
                true,
                true);
        }

        if (standardName is "空" or "荧")
        {
            return new CharacterSelectionTarget(
                standardName,
                [standardName],
                true,
                "单手剑",
                true,
                true);
        }

        var isOddity = standardName.StartsWith("奇偶", StringComparison.Ordinal);
        return new CharacterSelectionTarget(
            standardName,
            [standardName],
            isOddity,
            null,
            isOddity || standardName == "流浪者",
            isOddity);
    }

    public static (string? ElementType, string WeaponType) GetFilterTypes(
        CharacterSelectionTarget target,
        AvatarGridIconRecognizer recognizer)
    {
        var elementType = target.SkipElementFilter
            ? null
            : recognizer.GetElementType(target.PrimaryCandidateName);
        var weaponType = target.ForcedWeaponType
                         ?? recognizer.GetWeaponType(target.PrimaryCandidateName);
        return (elementType, weaponType);
    }

    public static bool IsFilterTagSelected(
        ImageRegion capture,
        string? text,
        double assetScale)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var ocrTexts = OcrFilterTags(capture, assetScale);
        var matched = ocrTexts.Any(ocrText => ocrText.Contains(text, StringComparison.Ordinal));
        return matched;
    }

    private static List<string> OcrFilterTags(ImageRegion capture, double assetScale)
    {
        using var tagRegion = capture.DeriveCrop(Rect1080(assetScale, 35, 910, 745, 55));
        using var hsv = tagRegion.SrcMat.CvtColor(ColorConversionCodes.BGR2HSV);
        using var darkMask = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 0), new Scalar(180, 95, 120), darkMask);
        using var binary = new Mat(tagRegion.SrcMat.Size(), MatType.CV_8UC3, Scalar.White);
        binary.SetTo(Scalar.Black, darkMask);
        var result = OcrFactory.Paddle.OcrResult(binary);
        return result.Regions
            .OrderBy(region => region.Rect.Center.Y)
            .ThenBy(region => region.Rect.Center.X)
            .Select(region => region.Text)
            .ToList();
    }

    public static bool IsFilterApplied(ImageRegion capture, double assetScale) =>
        ContainsText(capture, "清除", GetClearFilterRoi(assetScale));

    public static void ClearFilter(BvPage page, double assetScale, ILogger logger)
    {
        if (!TryClickText(page, "清除", GetClearFilterRoi(assetScale)))
        {
            logger.LogDebug("角色养成识别：未找到清除筛选按钮，继续执行");
        }
    }

    private static Rect GetClearFilterRoi(double assetScale) =>
        Rect1080(assetScale, 605, 925, 54, 28);

    public static bool IsFilterPanel(ImageRegion capture, double assetScale) =>
        ContainsText(capture, "确认筛选", GetConfirmFilterRoi(assetScale));

    public static Rect GetElementFilterOptionsRoi(double assetScale) =>
        Rect1080(assetScale, 35, 150, 745, 360);

    public static Rect GetWeaponFilterOptionsRoi(double assetScale) =>
        Rect1080(assetScale, 35, 560, 745, 280);

    public static Rect GetConfirmFilterRoi(double assetScale) =>
        Rect1080(assetScale, 310, 999, 128, 40);

    public static bool TryClickText(BvPage page, string text, Rect roi)
    {
        var regions = page.GetByText(text, roi).FindAll();
        try
        {
            var region = regions.OrderBy(item => item.Y).ThenBy(item => item.X).FirstOrDefault();
            if (region == null)
            {
                return false;
            }

            region.Click();
            return true;
        }
        finally
        {
            foreach (var region in regions)
            {
                region.Dispose();
            }
        }
    }

    private static bool ContainsText(ImageRegion capture, string text, Rect roi)
    {
        var regions = capture.FindMulti(RecognitionObject.Ocr(roi));
        try
        {
            var matched = regions.Any(region => region.Text.Contains(text, StringComparison.Ordinal));
            return matched;
        }
        finally
        {
            foreach (var region in regions)
            {
                region.Dispose();
            }
        }
    }

    /// <summary>
    /// 在角色列表中检测卡片、运行头像模型并点击目标角色；当前页未命中时继续向下滚动。
    /// </summary>
    public static async Task<AvatarGridIconCandidate?> FindAndClickAvatar(
        CharacterSelectionTarget target,
        AvatarGridIconRecognizer recognizer,
        double assetScale,
        ILogger logger,
        CancellationToken ct)
    {
        var gridParams = new GridParams(GridRoi1080, 5, 3, 40, 32, 0.024);
        var scroller = new GridScroller(gridParams, logger, Simulation.SendInput, ct);
        var gridRoi = Rect1080(assetScale, GridRoi1080.X, GridRoi1080.Y, GridRoi1080.Width, GridRoi1080.Height);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            for (var attempt = 1; attempt <= EmptyDetectionRetryCount; attempt++)
            {
                using var capture = CaptureToRectArea();
                using var gridRegion = capture.DeriveCrop(gridRoi);
                var cards = DetectCharacterCards(
                    gridRegion.SrcMat, assetScale, out var rejectedCount, out var connectedComponentCount);
                logger.LogDebug(
                    "角色养成识别：白色底栏连通域 {ConnectedComponentCount} 个，生成 {CardCount} 个合法卡片，边界丢弃 {RejectedCount} 个（第 {Attempt}/{RetryCount} 次）",
                    connectedComponentCount, cards.Count, rejectedCount, attempt, EmptyDetectionRetryCount);

                if (cards.Count == 0)
                {
                    if (attempt < EmptyDetectionRetryCount)
                    {
                        await Delay(200, ct);
                        continue;
                    }

                    throw new InvalidOperationException("未检测到合法角色卡片。");
                }

                foreach (var card in cards.OrderBy(card => card.CardRect.Y).ThenBy(card => card.CardRect.X))
                {
                    using var cardRegion = gridRegion.DeriveCrop(card.CardRect);
                    using var avatar = gridRegion.SrcMat.SubMat(card.AvatarRect);
                    using var element = gridRegion.SrcMat.SubMat(card.ElementRect);
                    var candidate = recognizer.Recognize(avatar, element, target.RecognizeElementType);
                    logger.LogDebug("角色养成识别：识别头像 {CharacterName}，元素={ElementType}，score={Score:0.000}",
                        candidate.CharacterName, candidate.ElementType, candidate.Score);
                    if (target.Matches(candidate.CharacterName) && candidate.Score >= MatchThreshold)
                    {
                        cardRegion.Click();
                        await Delay(300, ct);
                        return candidate;
                    }
                }

                break;
            }

            if (!await scroller.TryVerticalScollDown((src, _) =>
                {
                    var scrollCards = DetectCharacterCards(src, assetScale, out _, out _);
                    return scrollCards.Select(card => card.CardRect);
                }))
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 通过角色卡片底部白色区域检测当前画面中的真实卡片。
    /// </summary>
    /// <remarks>
    /// 流程与调参工具保持一致：HSV 二值化 → 5x5 Close → 8 邻域连通域 → 按白色像素面积筛选。
    /// <paramref name="connectedComponentCount"/> 不包含背景连通域；面积指连通域像素数，不是外接矩形面积。
    /// </remarks>
    internal static List<CharacterCardRect> DetectCharacterCards(
        Mat gridMat,
        double assetScale,
        out int rejectedCount,
        out int connectedComponentCount)
    {
        const int minWhiteRectArea1080 = 2400;
        const int maxWhiteRectArea1080 = 3000;

        using var hsv = gridMat.CvtColor(ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(20, 12, 233), new Scalar(35, 16, 237), mask);
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(5, 5));
        using var closedMask = new Mat();
        Cv2.MorphologyEx(mask, closedMask, MorphTypes.Close, kernel, iterations: 1);
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var labelCount = Cv2.ConnectedComponentsWithStats(
            closedMask,
            labels,
            stats,
            centroids,
            PixelConnectivity.Connectivity8,
            MatType.CV_32S);
        connectedComponentCount = Math.Max(0, labelCount - 1);

        var minArea = minWhiteRectArea1080 * assetScale * assetScale;
        var maxArea = maxWhiteRectArea1080 * assetScale * assetScale;
        var whiteRects = new List<Rect>();
        for (var label = 1; label < labelCount; label++)
        {
            var area = stats.At<int>(label, 4); // CC_STAT_AREA
            if (area < minArea || area > maxArea)
            {
                continue;
            }

            whiteRects.Add(new Rect(
                stats.At<int>(label, 0), // CC_STAT_LEFT
                stats.At<int>(label, 1), // CC_STAT_TOP
                stats.At<int>(label, 2), // CC_STAT_WIDTH
                stats.At<int>(label, 3))); // CC_STAT_HEIGHT
        }

        return BuildCharacterCards(whiteRects, gridMat.Size(), assetScale, out rejectedCount);
    }

    /// <summary>
    /// 根据白色底栏外接矩形的右下角反推固定尺寸角色卡片，并生成头像和元素裁剪区域。
    /// </summary>
    /// <remarks>
    /// 同行、同列的右边界和下边界会先用中位数校正，以消除二值化轮廓的轻微抖动；
    /// 反推后的卡片必须完整落在列表 ROI 内才会返回。
    /// </remarks>
    internal static List<CharacterCardRect> BuildCharacterCards(
        IReadOnlyList<Rect> whiteRects,
        Size gridSize,
        double assetScale,
        out int rejectedCount)
    {
        const int cardWidth1080 = 115;
        const int cardHeight1080 = 140;
        const int avatarSize1080 = 115;
        const int elementSize1080 = 48;

        rejectedCount = 0;
        if (whiteRects.Count == 0)
        {
            return [];
        }

        var cardWidth = Math.Max(1, (int)Math.Round(cardWidth1080 * assetScale));
        var cardHeight = Math.Max(1, (int)Math.Round(cardHeight1080 * assetScale));
        var avatarSize = Math.Max(1, (int)Math.Round(avatarSize1080 * assetScale));
        var elementSize = Math.Max(1, (int)Math.Round(elementSize1080 * assetScale));
        var columnTolerance = Math.Max(3, cardWidth / 3);
        var rowTolerance = Math.Max(3, cardHeight / 3);
        var rightValues = whiteRects.Select(rect => rect.Right).ToArray();
        var bottomValues = whiteRects.Select(rect => rect.Bottom).ToArray();
        var correctedRights = CorrectByMedian(rightValues, columnTolerance);
        var correctedBottoms = CorrectByMedian(bottomValues, rowTolerance);
        var cards = new List<CharacterCardRect>();
        var seen = new HashSet<(int Right, int Bottom)>();

        for (int i = 0; i < whiteRects.Count; i++)
        {
            var right = correctedRights[i];
            var bottom = correctedBottoms[i];
            if (!seen.Add((right, bottom)))
            {
                continue;
            }

            var cardRect = new Rect(right - cardWidth, bottom - cardHeight, cardWidth, cardHeight);
            if (cardRect.X < 0 || cardRect.Y < 0 || cardRect.Right > gridSize.Width || cardRect.Bottom > gridSize.Height)
            {
                rejectedCount++;
                continue;
            }

            cards.Add(new CharacterCardRect(
                cardRect,
                new Rect(cardRect.X, cardRect.Y, avatarSize, avatarSize),
                new Rect(cardRect.X, cardRect.Y, elementSize, elementSize)));
        }

        return cards;
    }

    /// <summary>
    /// 将距离不超过容差的同行或同列坐标归组，并用组内中位数替换原始值。
    /// </summary>
    private static int[] CorrectByMedian(IReadOnlyList<int> values, int tolerance)
    {
        var indexed = values.Select((value, index) => (Value: value, Index: index)).OrderBy(item => item.Value).ToList();
        var result = new int[values.Count];
        var group = new List<(int Value, int Index)>();

        void FlushGroup()
        {
            if (group.Count == 0)
            {
                return;
            }

            var ordered = group.Select(item => item.Value).OrderBy(value => value).ToArray();
            var median = ordered.Length % 2 == 1
                ? ordered[ordered.Length / 2]
                : (int)Math.Round((ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2d);
            foreach (var item in group)
            {
                result[item.Index] = median;
            }
            group.Clear();
        }

        foreach (var item in indexed)
        {
            if (group.Count > 0 && item.Value - group[^1].Value > tolerance)
            {
                FlushGroup();
            }
            group.Add(item);
        }
        FlushGroup();
        return result;
    }

    private static Rect Rect1080(double assetScale, int x, int y, int width, int height) =>
        new(
            (int)Math.Round(x * assetScale),
            (int)Math.Round(y * assetScale),
            (int)Math.Round(width * assetScale),
            (int)Math.Round(height * assetScale));

    private static string ToConfiguredAvatarName(string name)
    {
        if (DefaultAutoFightConfig.CombatAvatarMap.ContainsKey(name))
        {
            return name;
        }
        return DefaultAutoFightConfig.AvatarAliasToStandardName(name);
    }
}
