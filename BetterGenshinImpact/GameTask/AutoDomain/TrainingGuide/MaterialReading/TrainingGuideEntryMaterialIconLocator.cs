using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>入口的蓝色背景不能用饱和度分割，改用图标边框及同排等间距约束。</summary>
public static class TrainingGuideEntryMaterialIconLocator
{
    public static IReadOnlyList<Rect> FindIcons(Mat strip, double scale,
        Action<string>? diagnostic = null, Action<Mat, string>? saveDiagnostic = null)
    {
        diagnostic?.Invoke($"区域={strip.Width}x{strip.Height}；缩放={scale:F4}；数量范围=3～7；轮廓宽=70～125*scale，高=65～135*scale，宽高比=0.7～1.4，面积占比>=0.65");
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
            saveDiagnostic?.Invoke(edges, $"edges-{threshold}");
            var sizeRejected = 0;
            var areaRejected = 0;
            var duplicateRejected = 0;
            var initialCount = candidates.Count;
            foreach (var contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);
                if (rect.Width < 70 * scale || rect.Width > 125 * scale ||
                    rect.Height < 65 * scale || rect.Height > 135 * scale ||
                    rect.Width / (double)rect.Height is < .7 or > 1.4 ||
                    rect.X <= 1 || rect.Right >= strip.Width - 1 || rect.Y <= 1 || rect.Bottom >= strip.Height - 1)
                {
                    sizeRejected++;
                    if (rect.Width >= 50 * scale && rect.Height >= 50 * scale)
                        diagnostic?.Invoke($"轮廓阈值={threshold}；矩形={rect}；拒绝=尺寸/宽高比/触边；宽高比={rect.Width / (double)rect.Height:F4}");
                    continue;
                }
                if (Math.Abs(Cv2.ContourArea(contour)) < rect.Width * rect.Height * .65)
                {
                    areaRejected++;
                    diagnostic?.Invoke($"轮廓阈值={threshold}；矩形={rect}；拒绝=面积不足；面积占比={Math.Abs(Cv2.ContourArea(contour)) / (rect.Width * rect.Height):F4}");
                    continue;
                }
                if (candidates.Any(r => Math.Abs(CenterX(r) - CenterX(rect)) < 12 * scale &&
                    Math.Abs(CenterY(r) - CenterY(rect)) < 15 * scale))
                {
                    duplicateRejected++;
                    diagnostic?.Invoke($"轮廓阈值={threshold}；矩形={rect}；拒绝=与已有候选中心重叠");
                    continue;
                }
                candidates.Add(rect);
                diagnostic?.Invoke($"轮廓阈值={threshold}；接受候选={rect}");
            }
            diagnostic?.Invoke($"轮廓阈值={threshold}；轮廓总数={contours.Length}；尺寸/触边拒绝={sizeRejected}；面积拒绝={areaRejected}；重复拒绝={duplicateRejected}；新增={candidates.Count - initialCount}；累计={candidates.Count}");
        }

        if (saveDiagnostic != null)
        {
            using var overlay = strip.Clone();
            for (var i = 0; i < candidates.Count; i++)
            {
                Cv2.Rectangle(overlay, candidates[i], new Scalar(0, 255, 0), 1);
                Cv2.PutText(overlay, (i + 1).ToString(), new Point(candidates[i].X, Math.Max(12, candidates[i].Y)),
                    HersheyFonts.HersheySimplex, .4, new Scalar(0, 0, 255));
            }
            saveDiagnostic(overlay, "contour-candidates");
        }

        // 轮廓可能只覆盖通用奖励。无论候选数量是否合格，都读取竖边核对整排完整性。
        var verticalIcons = FindByVerticalEdges(strip, scale, diagnostic, saveDiagnostic, out var boundaries);
        // 两项固定奖励、可能出现的冒险阅历，加当前难度展示的1～4级材料，共3～7项。
        foreach (var anchor in candidates.OrderBy(r => r.Y))
        {
            var row = candidates.Where(r => Math.Abs(CenterY(r) - CenterY(anchor)) < 15 * scale &&
                Math.Abs(r.Width - anchor.Width) < 20 * scale).OrderBy(r => r.X).ToArray();
            if (row.Length is < 3 or > 7)
            {
                diagnostic?.Invoke($"同行锚点={anchor}；数量={row.Length}；拒绝=不在3～7范围");
                continue;
            }
            var gaps = row.Zip(row.Skip(1), (left, right) => CenterX(right) - CenterX(left)).ToArray();
            if (gaps.Any(g => g < 90 * scale || g > 140 * scale) || gaps.Max() - gaps.Min() > 16 * scale)
            {
                diagnostic?.Invoke($"同行锚点={anchor}；数量={row.Length}；间距=[{string.Join(",", gaps)}]；拒绝=间距不在90～140*scale或极差超过16*scale");
                continue;
            }
            var previousLeft = row[0].X - 110.5 * scale;
            var nextLeft = row[^1].X + 110.5 * scale;
            var hasPrevious = boundaries.Any(x => Math.Abs(x - previousLeft) <= 7 * scale);
            var hasNext = boundaries.Any(x => Math.Abs(x - nextLeft) <= 7 * scale);
            var verticalCoversRow = verticalIcons.Count >= row.Length && row.All(rect =>
                verticalIcons.Any(icon => Math.Abs(CenterX(icon) - CenterX(rect)) <= 12 * scale));
            if (hasPrevious || hasNext || (verticalCoversRow && verticalIcons.Count > row.Length))
            {
                diagnostic?.Invoke($"轮廓行不完整；数量={row.Length}；左侧额外边缘={hasPrevious}；右侧额外边缘={hasNext}；竖边数量={verticalIcons.Count}；竖边覆盖轮廓={verticalCoversRow}");
                if (verticalCoversRow && verticalIcons.Count > row.Length)
                {
                    diagnostic?.Invoke($"使用竖边补全整排：{row.Length} -> {verticalIcons.Count} 项");
                    return verticalIcons;
                }
                // 检出扩展边缘但无法补全时，不返回截断的轮廓行。
                diagnostic?.Invoke("发现额外图标边缘但未能确认完整行，定位失败，交由调用方重试");
                return Array.Empty<Rect>();
            }
            diagnostic?.Invoke($"轮廓定位成功，已检查两侧完整性；数量={row.Length}；矩形=[{string.Join(" | ", row)}]");
            return row;
        }
        diagnostic?.Invoke("轮廓未组成有效行，进入竖边兜底");
        return verticalIcons;
    }

    // 图标边框很淡，培养标记还会打断顶部轮廓。使用中段的彩色竖边，
    // 不要求边框闭合；底部“需求角色”横条不参与定位。
    private static IReadOnlyList<Rect> FindByVerticalEdges(Mat strip, double scale,
        Action<string>? diagnostic, Action<Mat, string>? saveDiagnostic, out List<int> boundaries)
    {
        boundaries = new List<int>();
        using var bgr = new Mat();
        if (strip.Channels() == 4) Cv2.CvtColor(strip, bgr, ColorConversionCodes.BGRA2BGR);
        else strip.CopyTo(bgr);
        var top = (int)Math.Round(20 * scale);
        var bottom = Math.Min(strip.Height, (int)Math.Round(80 * scale));
        var offset = Math.Max(1, (int)Math.Round(2 * scale));
        if (bottom <= top)
        {
            diagnostic?.Invoke($"竖边拒绝=采样高度不足；top={top}，bottom={bottom}");
            return Array.Empty<Rect>();
        }
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

        diagnostic?.Invoke($"竖边采样Y=[{top},{bottom})；偏移={offset}；对比阈值=10；命中比例>=0.7；边界X=[{string.Join(",", boundaries)}]；末尾未闭合边缘起点={runStart}；有效起点X< {70 * scale:F2}");
        if (saveDiagnostic != null)
        {
            using var overlay = bgr.Clone();
            foreach (var x in boundaries)
                Cv2.Line(overlay, new Point(x, top), new Point(x, bottom - 1), new Scalar(0, 0, 255));
            saveDiagnostic(overlay, "vertical-boundaries");
        }

        // 1080p 下图标约95px、间距约110px；所有位置仍需实际竖边确认。
        // 优先寻找更长的一排，低难度允许仅有3/4项。
        foreach (var count in new[] { 7, 6, 5, 4, 3 })
        foreach (var start in boundaries.Where(x => x < 70 * scale))
        {
            var icons = new List<Rect>();
            for (var index = 0; index < count; index++)
            {
                var expectedLeft = start + index * 110.5 * scale;
                var left = boundaries.Where(x => Math.Abs(x - expectedLeft) <= 5 * scale)
                    .OrderBy(x => Math.Abs(x - expectedLeft)).Select(x => (int?)x).FirstOrDefault();
                if (left == null)
                {
                    diagnostic?.Invoke($"竖边尝试数量={count}，起点={start}，位置={index + 1}；拒绝=缺少左边；预期X={expectedLeft:F2}±{5 * scale:F2}");
                    break;
                }
                var right = boundaries.Where(x => Math.Abs(x - (left.Value + 95 * scale)) <= 6 * scale)
                    .OrderBy(x => Math.Abs(x - (left.Value + 95 * scale))).Select(x => (int?)x).FirstOrDefault();
                if (right == null)
                {
                    diagnostic?.Invoke($"竖边尝试数量={count}，起点={start}，位置={index + 1}；左边={left}；拒绝=缺少右边；预期X={left.Value + 95 * scale:F2}±{6 * scale:F2}");
                    break;
                }
                var y = Math.Max(0, (int)Math.Round(4 * scale));
                var height = (int)Math.Round(95 * scale);
                if (y + height > strip.Height)
                {
                    diagnostic?.Invoke($"竖边拒绝=图标底部越界；y={y}，height={height}，区域高度={strip.Height}");
                    break;
                }
                icons.Add(new Rect(left.Value, y, right.Value - left.Value, height));
            }
            if (icons.Count != count) continue;
            // 仍有下一项竖边时不能把定位失败当作该难度不展示。
            if (boundaries.Any(x => Math.Abs(x - (start + count * 110.5 * scale)) <= 5 * scale))
            {
                diagnostic?.Invoke($"竖边尝试数量={count}，起点={start}；拒绝=仍存在下一项边缘；下一项预期X={start + count * 110.5 * scale:F2}");
                continue;
            }
            diagnostic?.Invoke($"竖边定位成功；数量={icons.Count}；矩形=[{string.Join(" | ", icons)}]");
            return icons;
        }
        diagnostic?.Invoke("定位失败：轮廓和竖边均未产生有效图标行");
        return Array.Empty<Rect>();
    }
    private static double CenterX(Rect rect) => rect.X + rect.Width / 2d;
    private static double CenterY(Rect rect) => rect.Y + rect.Height / 2d;
}
