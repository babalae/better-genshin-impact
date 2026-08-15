using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Model.GameUI;

internal enum FixedSizeGridCardLayout
{
    CharacterDevelopment,
    PartySetupCharacters
}

internal sealed record FixedSizeGridCardDetectionParams(
    int CardWidth1080,
    int CardHeight1080,
    int AvatarSize1080,
    int MinBottomArea1080,
    int MaxBottomArea1080);

internal sealed record FixedSizeGridCard(Rect CardRect, Rect AvatarRect);

/// <summary>
/// 通过卡片底部白色区域反推固定尺寸网格卡片。
/// </summary>
internal static class FixedSizeGridCardDetector
{
    private static readonly IReadOnlyDictionary<FixedSizeGridCardLayout, FixedSizeGridCardDetectionParams> LayoutParams =
        new Dictionary<FixedSizeGridCardLayout, FixedSizeGridCardDetectionParams>
        {
            [FixedSizeGridCardLayout.CharacterDevelopment] = new(115, 140, 115, 2000, 3000),
            [FixedSizeGridCardLayout.PartySetupCharacters] = new(132, 161, 132, 2500, 4000)
        };

    internal static List<FixedSizeGridCard> Detect(
        Mat gridMat,
        double assetScale,
        FixedSizeGridCardLayout layout,
        out int rejectedCount,
        out int connectedComponentCount)
    {
        var parameters = GetParams(layout);
        using var hsv = gridMat.CvtColor(ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(20, 12, 233), new Scalar(35, 16, 237), mask);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
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

        var minArea = parameters.MinBottomArea1080 * assetScale * assetScale;
        var maxArea = parameters.MaxBottomArea1080 * assetScale * assetScale;
        var bottomRects = new List<Rect>();
        for (var label = 1; label < labelCount; label++)
        {
            var area = stats.At<int>(label, 4);
            if (area < minArea || area > maxArea)
            {
                continue;
            }

            bottomRects.Add(new Rect(
                stats.At<int>(label, 0),
                stats.At<int>(label, 1),
                stats.At<int>(label, 2),
                stats.At<int>(label, 3)));
        }

        return BuildCards(bottomRects, gridMat.Size(), assetScale, layout, out rejectedCount);
    }

    internal static List<FixedSizeGridCard> BuildCards(
        IReadOnlyList<Rect> bottomRects,
        Size gridSize,
        double assetScale,
        FixedSizeGridCardLayout layout,
        out int rejectedCount)
    {
        var parameters = GetParams(layout);
        rejectedCount = 0;
        if (bottomRects.Count == 0)
        {
            return [];
        }

        var cardWidth = Math.Max(1, (int)Math.Round(parameters.CardWidth1080 * assetScale));
        var cardHeight = Math.Max(1, (int)Math.Round(parameters.CardHeight1080 * assetScale));
        var avatarSize = Math.Max(1, (int)Math.Round(parameters.AvatarSize1080 * assetScale));
        var columnTolerance = Math.Max(3, cardWidth / 3);
        var rowTolerance = Math.Max(3, cardHeight / 3);
        var correctedRights = CorrectByMedian(bottomRects.Select(rect => rect.Right).ToArray(), columnTolerance);
        var correctedBottoms = CorrectByMedian(bottomRects.Select(rect => rect.Bottom).ToArray(), rowTolerance);
        var cards = new List<FixedSizeGridCard>();
        var seen = new HashSet<(int Right, int Bottom)>();

        for (var i = 0; i < bottomRects.Count; i++)
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

            cards.Add(new FixedSizeGridCard(
                cardRect,
                new Rect(cardRect.X, cardRect.Y, avatarSize, avatarSize)));
        }

        return cards;
    }

    private static FixedSizeGridCardDetectionParams GetParams(FixedSizeGridCardLayout layout)
    {
        if (!LayoutParams.TryGetValue(layout, out var parameters))
        {
            throw new ArgumentOutOfRangeException(nameof(layout), layout, "未知的固定尺寸网格卡片布局");
        }

        return parameters;
    }

    private static int[] CorrectByMedian(IReadOnlyList<int> values, int tolerance)
    {
        var indexed = values
            .Select((value, index) => (Value: value, Index: index))
            .OrderBy(item => item.Value)
            .ToList();
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
}
