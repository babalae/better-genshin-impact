using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>L3 的几何部分：以方向索引产生候选，再用多锚点的多数一致性消除全局歧义。</summary>
internal sealed class BigMapAnchorGeometry
{
    /// <summary>无旋转地图中，点对方向不随缩放和平移改变。</summary>
    private const double AngleStep = 0.01;

    /// <summary>世界坐标网格单元大小，用于快速验证其余锚点。</summary>
    private const int CellSize = 256;

    /// <summary>允许图标中心存在少量模板定位误差。</summary>
    private const double PixelTolerance = 3;

    /// <summary>去重并排序后的世界地图锚点。</summary>
    private readonly Point2f[] _points;

    /// <summary>按方向分桶的世界点对；只用于提出假设，两个点永远不能直接判为成功。</summary>
    private readonly Dictionary<int, List<(int A, int B)>> _pairs = new();

    /// <summary>世界锚点的空间索引。</summary>
    private readonly Dictionary<(int X, int Y), List<int>> _cells = new();

    /// <summary>从只读参考锚点建立索引；不持有任何原生图像。</summary>
    internal BigMapAnchorGeometry(IEnumerable<Point2f> points, CancellationToken cancellationToken = default)
    {
        _points = points.Where(p => float.IsFinite(p.X) && float.IsFinite(p.Y)).Distinct()
            .OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        for (var i = 0; i < _points.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cell = Cell(_points[i].X, _points[i].Y);
            if (!_cells.TryGetValue(cell, out var indices))
            {
                _cells[cell] = indices = [];
            }
            indices.Add(i);

            for (var j = i + 1; j < _points.Length; j++)
            {
                var dx = _points[j].X - _points[i].X;
                var dy = _points[j].Y - _points[i].Y;
                if (dx * dx + dy * dy < 32 * 32)
                {
                    continue;
                }

                var bin = AngleBin(dx, dy);
                if (!_pairs.TryGetValue(bin, out var pairs))
                {
                    _pairs[bin] = pairs = [];
                }
                pairs.Add((i, j));
            }
        }
    }

    /// <summary>全局匹配；L1 有可靠校准时限制比例，无校准时仍可由多点几何独立定位。</summary>
    internal BigMapFeatureMatch? Match(IEnumerable<Point2f> detected, Size size, double? expectedScale = null,
        double scaleTolerance = 0.15, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = detected.Where(p => float.IsFinite(p.X) && float.IsFinite(p.Y)).Distinct()
            .OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (source.Length < 4 || _points.Length < 4 || size.Width <= 0 || size.Height <= 0 ||
            expectedScale is { } invalid && (!double.IsFinite(invalid) || invalid <= 0))
        {
            return null;
        }

        var hypotheses = new List<Hypothesis>();
        for (var i = 0; i < source.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var j = i + 1; j < source.Length; j++)
            {
                var dx = (double)source[j].X - source[i].X;
                var dy = (double)source[j].Y - source[i].Y;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared < 100 * 100)
                {
                    continue;
                }

                var angle = AngleBin(dx, dy);
                // 邻桶覆盖图标约三个像素的定位误差，随后再精确验证方向残差。
                for (var bin = angle - 3; bin <= angle + 3; bin++)
                {
                    if (!_pairs.TryGetValue(bin, out var pairs))
                    {
                        continue;
                    }

                    foreach (var (a, b) in pairs)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var tx = (double)_points[b].X - _points[a].X;
                        var ty = (double)_points[b].Y - _points[a].Y;
                        var scale = (tx * dx + ty * dy) / distanceSquared;
                        if (scale is < 0.08 or > 64 ||
                            expectedScale is { } expected && Math.Abs(scale / expected - 1) > scaleTolerance ||
                            Math.Abs(tx * dy - ty * dx) > PixelTolerance * scale * Math.Sqrt(distanceSquared))
                        {
                            continue;
                        }

                        var x = (_points[a].X + _points[b].X - scale * (source[i].X + source[j].X)) / 2;
                        var y = (_points[a].Y + _points[b].Y - scale * (source[i].Y + source[j].Y)) / 2;
                        var hypothesis = Verify(source, size, scale, x, y);
                        if (hypothesis != null)
                        {
                            // 同一视口只保留支持最多的拟合，不能让最先找到的噪声点对遮蔽更好的解。
                            var same = hypotheses.FindIndex(h => IsSameView(h, hypothesis.Scale, hypothesis.X, hypothesis.Y, size));
                            if (same < 0)
                            {
                                hypotheses.Add(hypothesis);
                            }
                            else if (hypothesis.Count > hypotheses[same].Count ||
                                     hypothesis.Count == hypotheses[same].Count && hypothesis.Error < hypotheses[same].Error)
                            {
                                hypotheses[same] = hypothesis;
                            }
                        }
                    }
                }
            }
        }

        var ordered = hypotheses.OrderByDescending(h => h.Count).ThenBy(h => h.Error).ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }

        var best = ordered[0];
        var alternative = ordered.Skip(1).FirstOrDefault(h => !IsSameView(h, best.Scale, best.X, best.Y, size));
        if (alternative != null && alternative.Count >= best.Count - 1)
        {
            return null;
        }

        return new BigMapFeatureMatch(new Rect2d(best.X, best.Y, size.Width * best.Scale, size.Height * best.Scale),
            best.Count / (double)source.Length);
    }

    /// <summary>验证唯一对应、空间覆盖和无旋转等比变换；返回重新拟合后的视口。</summary>
    private Hypothesis? Verify(Point2f[] source, Size size, double scale, double x, double y)
    {
        var matches = new List<(Point2f Query, Point2f World)>();
        var used = new HashSet<int>();
        foreach (var point in source)
        {
            var index = Nearest(point.X * scale + x, point.Y * scale + y, PixelTolerance * scale);
            if (index >= 0 && used.Add(index))
            {
                matches.Add((point, _points[index]));
            }
        }

        if (matches.Count < 4 || matches.Count < source.Length * 0.6 ||
            matches.Max(m => m.Query.X) - matches.Min(m => m.Query.X) < size.Width * 0.08 ||
            matches.Max(m => m.Query.Y) - matches.Min(m => m.Query.Y) < size.Height * 0.08)
        {
            return null;
        }

        // 同一直线上的重复标记不足以验证二维位置，必须有分散的非共线支持。
        var first = matches[0].Query;
        var area = matches.Max(a => matches.Max(b => Math.Abs(
            (a.Query.X - first.X) * (b.Query.Y - first.Y) - (a.Query.Y - first.Y) * (b.Query.X - first.X))));
        if (area < Math.Max(100, size.Width * size.Height * 0.001))
        {
            return null;
        }

        var qx = matches.Average(m => (double)m.Query.X);
        var qy = matches.Average(m => (double)m.Query.Y);
        var wx = matches.Average(m => (double)m.World.X);
        var wy = matches.Average(m => (double)m.World.Y);
        var variance = matches.Sum(m => Math.Pow(m.Query.X - qx, 2) + Math.Pow(m.Query.Y - qy, 2));
        scale = matches.Sum(m => (m.Query.X - qx) * (m.World.X - wx) + (m.Query.Y - qy) * (m.World.Y - wy)) / variance;
        x = wx - scale * qx;
        y = wy - scale * qy;
        var error = matches.Average(m => Math.Pow((m.Query.X * scale + x - m.World.X) / scale, 2) +
                                         Math.Pow((m.Query.Y * scale + y - m.World.Y) / scale, 2));
        return double.IsFinite(error) && error <= PixelTolerance * PixelTolerance
            ? new Hypothesis(scale, x, y, matches.Count, error) : null;
    }

    /// <summary>在邻近网格查找容差内最近的真实锚点。</summary>
    private int Nearest(double x, double y, double radius)
    {
        var cell = Cell(x, y);
        var best = -1;
        var distance = radius * radius;
        for (var cy = cell.Y - 1; cy <= cell.Y + 1; cy++)
        {
            for (var cx = cell.X - 1; cx <= cell.X + 1; cx++)
            {
                if (!_cells.TryGetValue((cx, cy), out var indices))
                {
                    continue;
                }
                foreach (var i in indices)
                {
                    var d = Math.Pow(_points[i].X - x, 2) + Math.Pow(_points[i].Y - y, 2);
                    if (d < distance)
                    {
                        distance = d;
                        best = i;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>以视口中心和比例合并同一解，保留相隔明显的竞争解用于歧义拒绝。</summary>
    private static bool IsSameView(Hypothesis h, double scale, double x, double y, Size size) =>
        Math.Abs(h.Scale / scale - 1) < 0.02 &&
        Math.Abs(h.X + h.Scale * size.Width / 2 - x - scale * size.Width / 2) < 5 * scale &&
        Math.Abs(h.Y + h.Scale * size.Height / 2 - y - scale * size.Height / 2) < 5 * scale;

    /// <summary>方向桶，输入点对从左到右排列。</summary>
    private static int AngleBin(double dx, double dy) => (int)Math.Floor((Math.Atan2(dy, dx) + Math.PI / 2) / AngleStep);

    /// <summary>世界坐标对应的空间网格。</summary>
    private static (int X, int Y) Cell(double x, double y) => ((int)Math.Floor(x / CellSize), (int)Math.Floor(y / CellSize));

    /// <summary>一个已被多点共同验证的定位假设。</summary>
    /// <param name="Scale">截图像素到 2048 级地图像素的比例。</param>
    /// <param name="X">完整视口左上角横坐标。</param>
    /// <param name="Y">完整视口左上角纵坐标。</param>
    /// <param name="Count">独立锚点对应数。</param>
    /// <param name="Error">截图像素下的均方残差。</param>
    private sealed record Hypothesis(double Scale, double X, double Y, int Count, double Error);
}
