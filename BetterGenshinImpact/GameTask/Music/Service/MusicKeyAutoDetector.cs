using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Music.Service;

/// <summary>
/// 从一张游戏截图里自动量出来的琴键阵列。**全部是比例坐标**（X 以画面宽度为 1，Y 以画面高度为 1），
/// 所以与分辨率、DPI、Windows 缩放都无关：换一台 1080P / 2K / 4K 的机器，同一张构图算出来的比例是一样的。
/// </summary>
public sealed record MusicDetectedKeyGrid(
    double OriginXFrac,
    double OriginYFrac,
    double ColumnSpacingFrac,
    double RowSpacingFrac,
    double RadiusFrac,
    double FrameWidth,
    double FrameHeight)
{
    /// <summary>基准分辨率，和 <c>MusicKeyLayout.ReferenceWidth</c> / <c>ReferenceHeight</c> 保持一致</summary>
    public const double ReferenceWidth = 1920d;

    /// <inheritdoc cref="ReferenceWidth" />
    public const double ReferenceHeight = 1080d;

    /// <summary>换算回「1080P 基准」的像素值，方便和配置里的默认值对照</summary>
    public double OriginX1080P => OriginXFrac * ReferenceWidth;

    /// <inheritdoc cref="OriginX1080P" />
    public double OriginY1080P => OriginYFrac * ReferenceHeight;

    /// <inheritdoc cref="OriginX1080P" />
    public double ColumnSpacing1080P => ColumnSpacingFrac * ReferenceWidth;

    /// <inheritdoc cref="OriginX1080P" />
    public double RowSpacing1080P => RowSpacingFrac * ReferenceHeight;

    /// <inheritdoc cref="OriginX1080P" />
    public double Radius1080P => RadiusFrac * ReferenceHeight;

    public string Describe()
    {
        return $"自动识别琴键成功：截图 {FrameWidth:F0}x{FrameHeight:F0}，" +
               $"换算到 1080P 基准 Q 键圆心 ({OriginX1080P:F1}, {OriginY1080P:F1})、" +
               $"列距 {ColumnSpacing1080P:F1}、排距 {RowSpacing1080P:F1}、键半径 {Radius1080P:F1}";
    }
}

/// <summary>
/// 在原神演奏界面的截图里找那 3 排 × 7 列的琴键。
///
/// 为什么需要它：琴键的实际位置由**游戏自己**的界面缩放决定，不同分辨率/宽高比/游戏版本都可能不一样，
/// 靠「1080P 基准 × 换算比例」推算总有偏差。这里直接把琴键从游戏画面里量出来，量不到才退回配置的默认值。
///
/// 识别思路（和 <c>_tmp/center_keys.py</c> 里验证过的算法一致）：
/// 1. 琴键是奶白色圆盘 —— 用「够亮 + 偏暖（R&gt;B）+ 不太饱和」三个条件做掩模，背景的蓝、角色、深色 UI 都被滤掉；
/// 2. 把掩模按行投影，找出 3 条「琴键排」的横带，再从三条带里挑等距的一组，得到每排的圆心 y；
/// 3. 把掩模按列投影，用「7 齿梳状匹配」在配置默认值附近搜索最合适的列距与起点（角色手/琴身挡住个别键也不影响整体匹配）；
/// 4. 每个齿的位置再本地取质心、最小二乘拟合一次，顺便量出琴键半径。
/// 任何一步不满足合理性检查就返回 null（调用方退回默认布局，不会画出更离谱的圈）。
/// </summary>
public static class MusicKeyAutoDetector
{
    /// <summary>演奏界面固定 3 排</summary>
    public const int ExpectedRowCount = 3;

    /// <summary>每排 7 个键</summary>
    public const int ExpectedColumnCount = 7;

    /// <summary>行投影超过画面宽度这个比例的，才算「这一行有琴键」</summary>
    private const double RowBandWidthRatio = 0.12;

    /// <summary>梳状匹配允许的列距搜索范围（相对先验值，留得宽一点，配置里的旧数值写歪了也还能认出来）</summary>
    private const double ColumnSpacingSearchMin = 0.5;

    /// <inheritdoc cref="ColumnSpacingSearchMin" />
    private const double ColumnSpacingSearchMax = 2.0;

    public static MusicDetectedKeyGrid? TryDetect(
        Mat frame,
        double expectedColumnSpacing = 167.4,
        double expectedRowSpacing = 134.5,
        double expectedRadius = 52d)
    {
        if (frame == null || frame.Empty() || frame.Width < 320 || frame.Height < 240)
        {
            return null;
        }

        var width = frame.Width;
        var height = frame.Height;

        // 配置里的期望值是按 1080P 记的；这里的截图可能已经被归一化到别的尺寸，先换算成当前画面的像素。
        var spacingPrior = expectedColumnSpacing * width / MusicDetectedKeyGrid.ReferenceWidth;
        var rowSpacingPrior = expectedRowSpacing * height / MusicDetectedKeyGrid.ReferenceHeight;
        var radiusPrior = expectedRadius * height / MusicDetectedKeyGrid.ReferenceHeight;
        if (spacingPrior < 8 || rowSpacingPrior < 8)
        {
            return null;
        }

        using var bgr = ToBgr(frame);
        using var mask = BuildKeyMask(bgr);
        var maskBytes = ToBytes(mask);

        var rowCounts = ProjectRows(maskBytes, width, height);
        var bands = FindRowBands(rowCounts, (int)(width * RowBandWidthRatio));
        var rows = PickThreeEvenBands(bands, rowCounts);
        if (rows == null)
        {
            return null;
        }

        var rowCenters = rows.Select(band => BandCenterY(band, rowCounts)).ToArray();
        var rowGaps = new[] { rowCenters[1] - rowCenters[0], rowCenters[2] - rowCenters[1] };
        var rowSpacing = (rowGaps[0] + rowGaps[1]) / 2d;
        if (rowSpacing <= 0 || Math.Abs(rowGaps[0] - rowGaps[1]) > rowSpacing * 0.25)
        {
            return null;
        }

        // 三排一起做列投影：三排是上下对齐的，叠起来信噪比更高。
        var columnProfile = ProjectColumns(maskBytes, width, rows);
        var comb = FitColumnComb(columnProfile, spacingPrior);
        if (comb == null)
        {
            return null;
        }

        var (originX, columnSpacing, centers) = comb.Value;
        if (columnSpacing < spacingPrior * 0.4 || columnSpacing > spacingPrior * 2.2)
        {
            return null;
        }

        var radius = MeasureRadius(columnProfile, centers, columnSpacing, radiusPrior);
        if (radius <= 0)
        {
            return null;
        }

        return new MusicDetectedKeyGrid(
            originX / width,
            rowCenters[0] / height,
            columnSpacing / width,
            rowSpacing / height,
            radius / height,
            width,
            height);
    }

    private static Mat ToBgr(Mat frame)
    {
        if (frame.Channels() == 3)
        {
            return frame.Clone();
        }

        var bgr = new Mat();
        Cv2.CvtColor(frame, bgr, frame.Channels() == 4 ? ColorConversionCodes.BGRA2BGR : ColorConversionCodes.GRAY2BGR);
        return bgr;
    }

    /// <summary>琴键掩模：够亮 + 偏暖 + 不花哨</summary>
    private static Mat BuildKeyMask(Mat bgr)
    {
        var channels = Cv2.Split(bgr);
        using var b = channels[0];
        using var g = channels[1];
        using var r = channels[2];

        using var minMat = new Mat();
        using var maxMat = new Mat();
        Cv2.Min(b, g, minMat);
        Cv2.Min(minMat, r, minMat);
        Cv2.Max(b, g, maxMat);
        Cv2.Max(maxMat, r, maxMat);

        using var brightness = new Mat();
        Cv2.Threshold(minMat, brightness, 150, 255, ThresholdTypes.Binary);

        using var warmDiff = new Mat();
        Cv2.Subtract(r, b, warmDiff);
        using var warm = new Mat();
        Cv2.Threshold(warmDiff, warm, 6, 255, ThresholdTypes.Binary);

        using var saturationRange = new Mat();
        Cv2.Subtract(maxMat, minMat, saturationRange);
        using var calm = new Mat();
        Cv2.Threshold(saturationRange, calm, 90, 255, ThresholdTypes.BinaryInv);

        var mask = new Mat();
        Cv2.BitwiseAnd(brightness, warm, mask);
        Cv2.BitwiseAnd(mask, calm, mask);

        foreach (var channel in channels)
        {
            channel.Dispose();
        }

        return mask;
    }

    /// <summary>把掩模取成 0/255 的字节数组，后面的投影都在 CPU 上直接算（比反复进 OpenCV 快）</summary>
    private static byte[] ToBytes(Mat mask)
    {
        var bytes = new byte[mask.Rows * mask.Cols];
        if (mask.IsContinuous())
        {
            Marshal.Copy(mask.Data, bytes, 0, bytes.Length);
            return bytes;
        }

        for (var y = 0; y < mask.Rows; y++)
        {
            Marshal.Copy(mask.Data + (int)(y * mask.Step()), bytes, y * mask.Cols, mask.Cols);
        }

        return bytes;
    }

    /// <summary>每一行有多少个琴键像素</summary>
    private static double[] ProjectRows(byte[] mask, int width, int height)
    {
        var counts = new double[height];
        for (var y = 0; y < height; y++)
        {
            var offset = y * width;
            var count = 0;
            for (var x = 0; x < width; x++)
            {
                if (mask[offset + x] > 0)
                {
                    count++;
                }
            }

            counts[y] = count;
        }

        return counts;
    }

    private readonly record struct RowBand(int Start, int End)
    {
        public int Height => End - Start + 1;
    }

    /// <summary>把行投影里连续的「有键区」并成横带</summary>
    private static List<RowBand> FindRowBands(double[] rowCounts, int threshold)
    {
        threshold = Math.Max(6, threshold);
        var bands = new List<RowBand>();
        var start = -1;
        var gap = 0;

        for (var y = 0; y < rowCounts.Length; y++)
        {
            if (rowCounts[y] >= threshold)
            {
                if (start < 0)
                {
                    start = y;
                }

                gap = 0;
            }
            else if (start >= 0)
            {
                gap++;
                if (gap >= 6)
                {
                    var end = y - gap;
                    if (end - start + 1 >= 6)
                    {
                        bands.Add(new RowBand(start, end));
                    }

                    start = -1;
                    gap = 0;
                }
            }
        }

        if (start >= 0)
        {
            var end = rowCounts.Length - 1 - gap;
            if (end - start + 1 >= 6)
            {
                bands.Add(new RowBand(start, end));
            }
        }

        return bands;
    }

    /// <summary>
    /// 从所有横带里挑出「等距的三条」。角色、琴身、场景里的白色物体都可能混出多余的带，
    /// 所以按面积加权 + 等距程度打分，而不是直接取前三条。
    /// </summary>
    private static RowBand[]? PickThreeEvenBands(List<RowBand> bands, double[] rowCounts)
    {
        if (bands.Count < ExpectedRowCount)
        {
            return null;
        }

        if (bands.Count == ExpectedRowCount)
        {
            return bands.ToArray();
        }

        var ordered = bands.OrderBy(x => x.Start).ToArray();
        var area = ordered.Select(band => rowCounts.Skip(band.Start).Take(band.Height).Sum()).ToArray();

        double bestScore = double.NegativeInfinity;
        RowBand[]? best = null;
        for (var i = 0; i < ordered.Length; i++)
        {
            for (var j = i + 1; j < ordered.Length; j++)
            {
                for (var k = j + 1; k < ordered.Length; k++)
                {
                    var c0 = (ordered[i].Start + ordered[i].End) / 2d;
                    var c1 = (ordered[j].Start + ordered[j].End) / 2d;
                    var c2 = (ordered[k].Start + ordered[k].End) / 2d;
                    var gapA = c1 - c0;
                    var gapB = c2 - c1;
                    if (gapA <= 0 || gapB <= 0)
                    {
                        continue;
                    }

                    var evenness = Math.Abs(gapA - gapB) / Math.Max(1d, (gapA + gapB) / 2d);
                    var score = area[i] + area[j] + area[k] - evenness * (area[i] + area[j] + area[k] + 1) * 2;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = [ordered[i], ordered[j], ordered[k]];
                    }
                }
            }
        }

        return best;
    }

    /// <summary>横带的圆心行：取计数接近峰值的那几行的重心（比取最高行更稳）</summary>
    private static double BandCenterY(RowBand band, double[] rowCounts)
    {
        var max = 0d;
        for (var y = band.Start; y <= band.End; y++)
        {
            max = Math.Max(max, rowCounts[y]);
        }

        var limit = max * 0.6;
        var sum = 0d;
        var weight = 0d;
        for (var y = band.Start; y <= band.End; y++)
        {
            if (rowCounts[y] >= limit)
            {
                sum += rowCounts[y] * y;
                weight += rowCounts[y];
            }
        }

        return weight > 0 ? sum / weight : (band.Start + band.End) / 2d;
    }

    /// <summary>把几排横带的琴键像素按列加起来</summary>
    private static double[] ProjectColumns(byte[] mask, int width, RowBand[] rows)
    {
        var profile = new double[width];
        foreach (var band in rows)
        {
            for (var y = band.Start; y <= band.End; y++)
            {
                var offset = y * width;
                for (var x = 0; x < width; x++)
                {
                    if (mask[offset + x] > 0)
                    {
                        profile[x]++;
                    }
                }
            }
        }

        return profile;
    }

    /// <summary>
    /// 7 齿梳状匹配：找一组 (起点, 列距)，让 7 个齿都落在琴键的列投影峰上。
    /// 先粗搜（列距在默认值 0.6~1.7 倍之间步进），再对每个齿取局部质心做最小二乘精修。
    /// </summary>
    private static (double OriginX, double ColumnSpacing, double[] Centers)? FitColumnComb(double[] profile, double spacingPrior)
    {
        var width = profile.Length;
        var minSpacing = Math.Max(6d, spacingPrior * ColumnSpacingSearchMin);
        var maxSpacing = spacingPrior * ColumnSpacingSearchMax;
        if (width < minSpacing * ExpectedColumnCount)
        {
            return null;
        }

        var step = Math.Max(0.5, spacingPrior * 0.01);
        var bestScore = double.NegativeInfinity;
        var bestOrigin = 0d;
        var bestSpacing = 0d;

        for (var spacing = minSpacing; spacing <= maxSpacing; spacing += step)
        {
            var window = spacing * 0.12;
            var span = spacing * (ExpectedColumnCount - 1);
            for (var origin = 0d; origin + span < width; origin += 2d)
            {
                var score = 0d;
                for (var i = 0; i < ExpectedColumnCount; i++)
                {
                    score += WindowSum(profile, origin + i * spacing, window);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestOrigin = origin;
                    bestSpacing = spacing;
                }
            }
        }

        if (bestScore <= 0)
        {
            return null;
        }

        // 精修：每个齿附近取质心（取两轮，第一轮把搜索窗摆正，第二轮才不会被窗口边界带偏），
        // 然后最小二乘拟合 (起点, 列距)
        var centers = new double[ExpectedColumnCount];
        for (var i = 0; i < ExpectedColumnCount; i++)
        {
            var seed = bestOrigin + i * bestSpacing;
            var center = LocalCentroid(profile, seed, bestSpacing * 0.45);
            if (center < 0)
            {
                return null;
            }

            center = LocalCentroid(profile, center, bestSpacing * 0.45);
            if (center < 0)
            {
                return null;
            }

            centers[i] = center;
        }

        var meanIndex = (ExpectedColumnCount - 1) / 2d;
        var meanCenter = centers.Average();
        var numerator = 0d;
        var denominator = 0d;
        for (var i = 0; i < ExpectedColumnCount; i++)
        {
            var di = i - meanIndex;
            numerator += di * (centers[i] - meanCenter);
            denominator += di * di;
        }

        var spacingFit = numerator / denominator;
        if (spacingFit <= 0)
        {
            return null;
        }

        var originFit = meanCenter - spacingFit * meanIndex;

        // 拟合残差太大说明配到的不是整齐的七列
        for (var i = 0; i < ExpectedColumnCount; i++)
        {
            var predicted = originFit + i * spacingFit;
            if (Math.Abs(predicted - centers[i]) > spacingFit * 0.25)
            {
                return null;
            }
        }

        return (originFit, spacingFit, centers);
    }

    private static double WindowSum(double[] profile, double center, double halfWindow)
    {
        var from = Math.Max(0, (int)Math.Round(center - halfWindow));
        var to = Math.Min(profile.Length - 1, (int)Math.Round(center + halfWindow));
        var sum = 0d;
        for (var x = from; x <= to; x++)
        {
            sum += profile[x];
        }

        return sum;
    }

    /// <summary>在 center 邻域里按能量取质心</summary>
    private static double LocalCentroid(double[] profile, double center, double halfWindow)
    {
        var from = Math.Max(0, (int)Math.Round(center - halfWindow));
        var to = Math.Min(profile.Length - 1, (int)Math.Round(center + halfWindow));
        var sum = 0d;
        var weight = 0d;
        for (var x = from; x <= to; x++)
        {
            sum += profile[x] * x;
            weight += profile[x];
        }

        return weight > 0 ? sum / weight : -1;
    }

    /// <summary>用每个齿左右两侧的投影宽度量琴键半径</summary>
    private static double MeasureRadius(double[] profile, double[] centers, double columnSpacing, double radiusPrior)
    {
        var max = profile.Max();
        if (max <= 0)
        {
            return 0;
        }

        var limit = max * 0.15;
        var widths = new List<double>();
        foreach (var center in centers)
        {
            var from = Math.Max(0, (int)Math.Round(center - columnSpacing * 0.5));
            var to = Math.Min(profile.Length - 1, (int)Math.Round(center + columnSpacing * 0.5));
            var left = center;
            while (left > from && profile[(int)Math.Round(left)] >= limit)
            {
                left -= 1;
            }

            var right = center;
            while (right < to && profile[(int)Math.Round(right)] >= limit)
            {
                right += 1;
            }

            var width = right - left;
            if (width > 0)
            {
                widths.Add(width);
            }
        }

        if (widths.Count < ExpectedColumnCount / 2)
        {
            return 0;
        }

        widths.Sort();
        var radius = widths[widths.Count / 2] / 2d;
        if (radius <= 0 || radius > columnSpacing * 0.75)
        {
            return 0;
        }

        // 和预期差太多说明量到的不是琴键
        if (radiusPrior > 0 && (radius < radiusPrior * 0.45 || radius > radiusPrior * 2.2))
        {
            return 0;
        }

        return radius;
    }
}
