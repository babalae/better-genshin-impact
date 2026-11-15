using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using OpenCvSharp;
using OpenCvSharp.Flann;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>符合游戏大地图约束的匹配结果。</summary>
/// <param name="Viewport">查询图像在训练图像中的完整视口。</param>
/// <param name="Confidence">几何内点比例。</param>
internal sealed record BigMapFeatureMatch(Rect2d Viewport, double Confidence);

/// <summary>通过多点一致性验证等比缩放和平移，拒绝退化单应、旋转和局部图标误匹配。</summary>
internal static class BigMapFeatureGeometry
{
    /// <summary>比例测试后去重，再以 RANSAC 验证；不重复提取查询图特征。</summary>
    public static BigMapFeatureMatch? Match(BigMapImageFeatures query, KeyPoint[] trainPoints, Mat trainDescriptors,
        double minimumInlierRatio = 0.4, Size? trainImageSize = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (query.Descriptors.Empty() || trainDescriptors.Empty() || trainPoints.Length < 8)
        {
            return null;
        }

        using var matcher = CreateMatcher();
        matcher.Add([trainDescriptors]);
        return Match(query, trainPoints, matcher, minimumInlierRatio, trainImageSize, cancellationToken);
    }

    /// <summary>提高近邻搜索精度，减少稀疏地形因近似搜索漏掉有效对应点而出现帧间抖动。</summary>
    internal static FlannBasedMatcher CreateMatcher()
    {
        using var index = new KDTreeIndexParams(4);
        using var search = new SearchParams(128);
        return new FlannBasedMatcher(index, search);
    }

    /// <summary>在几何对齐后验证灰度外观，拒绝从其它层的暗色背景认出当前区域却误报层名。</summary>
    internal static (double Correlation, double MeanDifference) CompareAppearance(
        BigMapImageFeatures query, BigMapImageFeatures target, Rect2d viewport, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 素材与截图的比例可能超过 6 倍，必须先重采样到同一尺度，再用相同核抑制图标和文字。
        // 分别在各自尺度平滑后逐点比较，会把正确的小缩放地图误判为外观不符。
        const int blurSize = 21;
        var scale = viewport.Width / query.Size.Width;
        using var transform = new Mat(2, 3, MatType.CV_64FC1, Scalar.All(0));
        transform.Set(0, 0, scale);
        transform.Set(1, 1, scale);
        transform.Set(0, 2, viewport.X);
        transform.Set(1, 2, viewport.Y);
        using var aligned = new Mat();
        using var queryAppearance = new Mat();
        using var targetAppearance = new Mat();
        Cv2.WarpAffine(target.Appearance, aligned, transform, query.Size,
            InterpolationFlags.Linear | InterpolationFlags.WarpInverseMap, BorderTypes.Replicate);
        Cv2.GaussianBlur(query.Appearance, queryAppearance, new Size(blurSize, blurSize), 0);
        Cv2.GaussianBlur(aligned, targetAppearance, new Size(blurSize, blurSize), 0);

        double sumQuery = 0, sumTarget = 0, sumQuerySquare = 0, sumTargetSquare = 0, sumProduct = 0, difference = 0;
        var count = 0;
        var foregroundCount = 0;
        var step = Math.Max(1, query.Size.Width / 160);
        // 只采样中央地图区域，避开左侧任务栏、顶部资源栏和右侧地图按钮。
        for (var y = query.Size.Height / 10; y < query.Size.Height * 9 / 10; y += step)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = query.Size.Width / 5; x < query.Size.Width * 93 / 100; x += step)
            {
                // 平滑核也须位于地图中，不能将面板浅色背景混入边缘的地图采样。
                if (x < query.MapArea.Left + blurSize / 2 || x >= query.MapArea.Right - blurSize / 2)
                {
                    continue;
                }
                var tx = (int)Math.Round(viewport.X + x * viewport.Width / query.Size.Width);
                var ty = (int)Math.Round(viewport.Y + y * viewport.Height / query.Size.Height);
                // 只比较卷积核完全落在素材内的区域，避免层图边缘的填充色污染统计。
                var margin = blurSize / 2d * scale;
                if (tx < margin || ty < margin || tx >= target.Size.Width - margin || ty >= target.Size.Height - margin)
                {
                    continue;
                }

                var q = (double)queryAppearance.At<byte>(y, x);
                var t = (double)targetAppearance.At<byte>(y, x);
                if (t > 110)
                {
                    foregroundCount++;
                }
                sumQuery += q;
                sumTarget += t;
                sumQuerySquare += q * q;
                sumTargetSquare += t * t;
                sumProduct += q * t;
                difference += Math.Abs(q - t);
                count++;
            }
        }

        var variance = (count * sumQuerySquare - sumQuery * sumQuery) *
                       (count * sumTargetSquare - sumTarget * sumTarget);
        // 未选层的暗色背景也能准确定位；必须看到足够的亮色层主体才能确认层名。
        // 只露出边角暗地形的小层不具备楼层身份的证据，不能抢先返回成功。
        if (count < 64 || foregroundCount < 16 || variance <= 0)
        {
            return (0, 255);
        }

        var correlation = (count * sumProduct - sumQuery * sumTarget) / Math.Sqrt(variance);
        return (correlation, difference / count);
    }

    /// <summary>复用已建立的训练索引，只匹配当前帧的查询描述子。</summary>
    public static BigMapFeatureMatch? Match(BigMapImageFeatures query, KeyPoint[] trainPoints, FlannBasedMatcher matcher,
        double minimumInlierRatio = 0.4, Size? trainImageSize = null, CancellationToken cancellationToken = default)
    {
        var accepted = new List<DMatch>();
        // 分批调用原生近邻检索，使地图移动后能在一小批描述子完成时退出。
        for (var offset = 0; offset < query.Descriptors.Rows; offset += 128)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var batch = query.Descriptors.RowRange(offset, Math.Min(offset + 128, query.Descriptors.Rows));
            foreach (var neighbors in matcher.KnnMatch(batch, 2))
            {
                if (neighbors.Length == 2 && neighbors[0].Distance < 0.7 * neighbors[1].Distance)
                {
                    var match = neighbors[0];
                    accepted.Add(new DMatch(match.QueryIdx + offset, match.TrainIdx, match.Distance));
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var matches = accepted.GroupBy(m => m.TrainIdx).Select(g => g.MinBy(m => m.Distance)).ToArray();
        return Fit(matches.Select(m => query.KeyPoints[m.QueryIdx].Pt).ToArray(),
            matches.Select(m => trainPoints[m.TrainIdx].Pt).ToArray(), query.Size, minimumInlierRatio, trainImageSize, cancellationToken);
    }

    /// <summary>先估计相似变换，再验证独立内点数、空间覆盖及轴对齐残差。</summary>
    internal static BigMapFeatureMatch? Fit(Point2f[] source, Point2f[] target, Size querySize,
        double minimumInlierRatio = 0.4, Size? trainImageSize = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Length < 8 || source.Length != target.Length || querySize.Width <= 0 || querySize.Height <= 0 ||
            source.Concat(target).Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y)))
        {
            return null;
        }

        using var sourceMat = InputArray.Create(source);
        using var targetMat = InputArray.Create(target);
        using var mask = new Mat();
        using var transform = Cv2.EstimateAffinePartial2D(sourceMat, targetMat, mask, RobustEstimationAlgorithms.RANSAC, 2);
        cancellationToken.ThrowIfCancellationRequested();
        if (transform.Empty())
        {
            return null;
        }

        var scale = transform.At<double>(0, 0);
        var rotation = transform.At<double>(1, 0);
        if (!double.IsFinite(scale) || scale <= 0.02 || scale > 16 || Math.Abs(rotation) > scale * 0.02)
        {
            return null;
        }

        var indices = Enumerable.Range(0, source.Length).Where(i => mask.At<byte>(i) != 0).ToArray();
        if (indices.Length < 8 || indices.Length < source.Length * minimumInlierRatio)
        {
            return null;
        }

        var points = indices.Select(i => source[i]).ToArray();
        // 小层图在整张截图中可能只占几十像素，应相对其可见范围检查覆盖，而非相对整屏。
        var coveredWidth = Math.Min(querySize.Width, trainImageSize?.Width / scale ?? querySize.Width);
        var coveredHeight = Math.Min(querySize.Height, trainImageSize?.Height / scale ?? querySize.Height);
        if (points.Max(p => p.X) - points.Min(p => p.X) < Math.Max(12, coveredWidth * 0.08) ||
            points.Max(p => p.Y) - points.Min(p => p.Y) < Math.Max(12, coveredHeight * 0.08))
        {
            return null;
        }

        // 游戏地图没有旋转和透视；用内点重新拟合一个缩放值和两个平移值。
        var sx = points.Average(p => (double)p.X);
        var sy = points.Average(p => (double)p.Y);
        var tx = indices.Average(i => (double)target[i].X);
        var ty = indices.Average(i => (double)target[i].Y);
        var variance = points.Sum(p => Math.Pow(p.X - sx, 2) + Math.Pow(p.Y - sy, 2));
        if (variance < 1)
        {
            return null;
        }

        scale = indices.Sum(i => (source[i].X - sx) * (target[i].X - tx) +
                                 (source[i].Y - sy) * (target[i].Y - ty)) / variance;
        var dx = tx - scale * sx;
        var dy = ty - scale * sy;
        var error = indices.Average(i => Math.Pow(source[i].X * scale + dx - target[i].X, 2) +
                                         Math.Pow(source[i].Y * scale + dy - target[i].Y, 2));
        if (!double.IsFinite(error) || error > 4 || scale <= 0.02 || scale > 16)
        {
            return null;
        }

        return new BigMapFeatureMatch(new Rect2d(dx, dy, querySize.Width * scale, querySize.Height * scale),
            indices.Length / (double)source.Length);
    }
}
