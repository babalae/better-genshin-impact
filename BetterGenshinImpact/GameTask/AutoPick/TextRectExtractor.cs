using System;

namespace BetterGenshinImpact.GameTask.AutoPick;

using OpenCvSharp;
using System.Collections.Generic;
using System.Linq;

public static class TextRectExtractor
{
    public static Rect GetTextBoundingRect(Mat src, double minContourArea = 25, int mergeGap = 6)
    {
        var rects = GetTextBoundingRects(src, minContourArea, mergeGap);
        if (rects.Count == 0)
            return new Rect();

        // 只保留 X <= 20 的矩形, 并取最左(再按 Y 排)
        var candidate = rects
            .Where(r => r.X <= 20)
            .OrderBy(r => r.X)
            .ThenBy(r => r.Y)
            .FirstOrDefault();

        return candidate;
    }

    // 可选: 单独提供一个方法, 可自定义阈值
    public static Rect GetLeftMostTextRect(Mat src, double minContourArea = 25, int mergeGap = 6, int maxAcceptedX = 20)
    {
        var rects = GetTextBoundingRects(src, minContourArea, mergeGap);
        if (rects.Count == 0)
            return new Rect();

        var candidate = rects
            .Where(r => r.X <= maxAcceptedX)
            .OrderBy(r => r.X)
            .ThenBy(r => r.Y)
            .FirstOrDefault();

        return candidate;
    }

    public static List<Rect> GetTextBoundingRects(Mat src, double minContourArea = 25, int mergeGap = 6)
    {
        var result = new List<Rect>();
        if (src == null || src.Empty())
            return result;

        using var gray = src.Channels() == 1 ? src.Clone() : src.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var blur = gray.GaussianBlur(new Size(3, 3), 0);

        using var binary = new Mat();
        Cv2.Threshold(blur, binary, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);

        var whiteRatio = Cv2.CountNonZero(binary) / (double)(binary.Rows * binary.Cols);
        if (whiteRatio > 0.65)
        {
            Cv2.BitwiseNot(binary, binary);
        }

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel, iterations: 1);
        Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel, iterations: 1);

        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        foreach (var c in contours)
        {
            var area = Cv2.ContourArea(c);
            if (area < minContourArea)
                continue;
            var r = Cv2.BoundingRect(c);
            if (r.Width < 3 || r.Height < 3)
                continue;
            if (r.Width / (double)r.Height > 40)
                continue;
            result.Add(r);
        }

        if (result.Count == 0)
        {
            using var grad = new Mat();
            Cv2.Sobel(gray, grad, MatType.CV_8U, 1, 0, ksize: 3);
            Cv2.Threshold(grad, grad, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
            Cv2.MorphologyEx(grad, grad, MorphTypes.Dilate, kernel, iterations: 1);
            Cv2.FindContours(grad, out var gContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (var c in gContours)
            {
                var area = Cv2.ContourArea(c);
                if (area < minContourArea)
                    continue;
                var r = Cv2.BoundingRect(c);
                if (r.Width < 3 || r.Height < 3) continue;
                result.Add(r);
            }
        }

        if (result.Count == 0)
            return result;

        result = result
            .OrderBy(r => r.Y)
            .ThenBy(r => r.X)
            .ToList();

        result = MergeRects(result, mergeGap);

        if (result.Count == 1)
        {
            var r = result[0];
            double fill = (r.Width * r.Height) / (double)(src.Cols * src.Rows);
            if (fill > 0.97)
                return new List<Rect>();
        }

        return result;
    }

    private static List<Rect> MergeRects(List<Rect> rects, int gap)
    {
        if (rects.Count <= 1) return rects;

        var merged = new List<Rect>();
        Rect current = rects[0];

        for (int i = 1; i < rects.Count; i++)
        {
            var r = rects[i];
            bool sameLine = IsSameLine(current, r, lineTolerance: Math.Max(6, (current.Height + r.Height) / 4));
            bool close = r.X <= current.Right + gap;

            if (sameLine && close)
            {
                current = current | r;
            }
            else
            {
                merged.Add(current);
                current = r;
            }
        }
        merged.Add(current);
        return merged;
    }

    private static bool IsSameLine(Rect a, Rect b, int lineTolerance)
    {
        int centerA = a.Y + a.Height / 2;
        int centerB = b.Y + b.Height / 2;
        return Math.Abs(centerA - centerB) <= lineTolerance;
    }
}
