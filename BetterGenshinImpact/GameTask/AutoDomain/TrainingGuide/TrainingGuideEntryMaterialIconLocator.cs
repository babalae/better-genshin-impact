using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>入口的蓝色背景不能用饱和度分割，改用图标边框及同排等间距约束。</summary>
public static class TrainingGuideEntryMaterialIconLocator
{
    public static IReadOnlyList<Rect> FindIcons(Mat strip, double scale)
    {
        using var gray = new Mat();
        Cv2.CvtColor(strip, gray, strip.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        var candidates = new List<Rect>();
        foreach (var threshold in new[] { 25, 50, 85 })
        {
            using var edges = new Mat();
            Cv2.Canny(gray, edges, threshold, threshold * 2);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.MorphologyEx(edges, edges, MorphTypes.Close, kernel);
            Cv2.FindContours(edges, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);
            foreach (var contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);
                if (rect.Width < 70 * scale || rect.Width > 125 * scale ||
                    rect.Height < 65 * scale || rect.Height > 135 * scale ||
                    rect.Width / (double)rect.Height is < .7 or > 1.4 ||
                    rect.X <= 1 || rect.Right >= strip.Width - 1 || rect.Y <= 1 || rect.Bottom >= strip.Height - 1)
                    continue;
                if (Math.Abs(Cv2.ContourArea(contour)) < rect.Width * rect.Height * .65) continue;
                if (candidates.Any(r => Math.Abs(CenterX(r) - CenterX(rect)) < 12 * scale &&
                    Math.Abs(CenterY(r) - CenterY(rect)) < 15 * scale)) continue;
                candidates.Add(rect);
            }
        }

        // 同排完整图标必须是5或6个：两项固定奖励，加3/4级材料。不通过则不点击。
        foreach (var anchor in candidates.OrderBy(r => r.Y))
        {
            var row = candidates.Where(r => Math.Abs(CenterY(r) - CenterY(anchor)) < 15 * scale &&
                Math.Abs(r.Width - anchor.Width) < 20 * scale).OrderBy(r => r.X).ToArray();
            if (row.Length is not (5 or 6)) continue;
            var gaps = row.Zip(row.Skip(1), (left, right) => CenterX(right) - CenterX(left)).ToArray();
            if (gaps.Any(g => g < 90 * scale || g > 140 * scale) || gaps.Max() - gaps.Min() > 16 * scale) continue;
            return row;
        }
        return FindByVerticalEdges(strip, scale);
    }

    // 图标边框很淡，培养标记还会打断顶部轮廓。使用中段的彩色竖边，
    // 不要求边框闭合；底部“需求角色”横条不参与定位。
    private static IReadOnlyList<Rect> FindByVerticalEdges(Mat strip, double scale)
    {
        using var bgr = new Mat();
        if (strip.Channels() == 4) Cv2.CvtColor(strip, bgr, ColorConversionCodes.BGRA2BGR);
        else strip.CopyTo(bgr);
        var top = (int)Math.Round(20 * scale);
        var bottom = Math.Min(strip.Height, (int)Math.Round(80 * scale));
        var offset = Math.Max(1, (int)Math.Round(2 * scale));
        if (bottom <= top) return Array.Empty<Rect>();
        var boundaries = new List<int>();
        var runStart = -1;
        for (var x = offset; x < strip.Width - offset; x++)
        {
            var hits = 0;
            for (var y = top; y < bottom; y++)
            {
                var left = bgr.At<Vec3b>(y, x - offset);
                var right = bgr.At<Vec3b>(y, x + offset);
                var contrast = Math.Max(Math.Abs(left.Item0 - right.Item0),
                    Math.Max(Math.Abs(left.Item1 - right.Item1), Math.Abs(left.Item2 - right.Item2)));
                if (contrast >= 10) hits++;
            }
            var edge = hits >= (bottom - top) * .7;
            if (edge && runStart < 0) runStart = x;
            if (!edge && runStart >= 0)
            {
                boundaries.Add((runStart + x - 1) / 2);
                runStart = -1;
            }
        }

        // 1080p 下图标约95px、间距约110px；所有位置仍需实际竖边确认。
        // 优先寻找完整六项，避免把武器材料的末级误当成不存在。
        foreach (var count in new[] { 6, 5 })
        foreach (var start in boundaries.Where(x => x < 70 * scale))
        {
            var icons = new List<Rect>();
            for (var index = 0; index < count; index++)
            {
                var expectedLeft = start + index * 110.5 * scale;
                var left = boundaries.Where(x => Math.Abs(x - expectedLeft) <= 5 * scale)
                    .OrderBy(x => Math.Abs(x - expectedLeft)).Select(x => (int?)x).FirstOrDefault();
                if (left == null) break;
                var right = boundaries.Where(x => Math.Abs(x - (left.Value + 95 * scale)) <= 6 * scale)
                    .OrderBy(x => Math.Abs(x - (left.Value + 95 * scale))).Select(x => (int?)x).FirstOrDefault();
                if (right == null) break;
                var y = Math.Max(0, (int)Math.Round(4 * scale));
                var height = (int)Math.Round(95 * scale);
                if (y + height > strip.Height) break;
                icons.Add(new Rect(left.Value, y, right.Value - left.Value, height));
            }
            if (icons.Count != count) continue;
            // 仍有下一项竖边时不能将不完整的六项降级为五项。
            if (count == 5 && boundaries.Any(x => Math.Abs(x - (start + 5 * 110.5 * scale)) <= 5 * scale))
                continue;
            return icons;
        }
        return Array.Empty<Rect>();
    }
    private static double CenterX(Rect rect) => rect.X + rect.Width / 2d;
    private static double CenterY(Rect rect) => rect.Y + rect.Height / 2d;
}
