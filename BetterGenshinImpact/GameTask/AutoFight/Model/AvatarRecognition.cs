using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 战斗识别相关的通用工具函数
/// </summary>
public partial class Avatar
{
    /// <summary>
    /// 传奇血条动态追踪字典：2px粒度的 y → 连续出现计数。
    /// y 96-200 范围的血条在连续5帧中出现时被标记为传奇。
    /// </summary>
    private static readonly Dictionary<int, int> _legendaryBarTracker = new();
    private const int LegendaryBarTrackThreshold = 5;

    /// <summary>
    /// 更新传奇血条动态追踪状态。
    /// 对 y 96-200 的血条进行帧间连续性追踪，连续出现达到阈值后标记为传奇。
    /// 允许1帧容错：某帧未出现时计数递减而非直接清零。
    /// </summary>
    private static void UpdateLegendaryBarTracker(IEnumerable<int> barYs)
    {
        var currentBins = barYs.Where(y => y >= 96 && y < 200)
                               .Select(y => y / 2 * 2)
                               .ToHashSet();

        // 存在的 y：递增（上限为阈值）
        foreach (var bin in currentBins)
        {
            if (_legendaryBarTracker.TryGetValue(bin, out var cnt))
                _legendaryBarTracker[bin] = Math.Min(cnt + 1, LegendaryBarTrackThreshold);
            else
                _legendaryBarTracker[bin] = 1;
        }

        // 不存在的 y：递减（1帧容错），归零则移除
        foreach (var bin in _legendaryBarTracker.Keys.ToArray())
        {
            if (!currentBins.Contains(bin))
            {
                _legendaryBarTracker[bin]--;
                if (_legendaryBarTracker[bin] <= 0)
                    _legendaryBarTracker.Remove(bin);
            }
        }
    }

    /// <summary>
    /// 判断指定 y 坐标的血条是否为传奇血条。
    /// y < 96 直接判定为传奇；y 96-200 使用动态追踪结果；y >= 200 视为普通。
    /// </summary>
    private static bool IsLegendaryBar(int y)
    {
        if (y < 96) return true;
        if (y >= 200) return false;
        return _legendaryBarTracker.TryGetValue(y / 2 * 2, out var cnt) && cnt >= LegendaryBarTrackThreshold;
    }

    /// <summary>
    /// 检测屏幕中的红色血条（连通域分析）
    /// </summary>
    public static List<(int x, int y, int width, int height)> FindBloodBars(ImageRegion? existingCapture = null)
    {
        var results = new List<(int x, int y, int width, int height)>();

        using var image = existingCapture ?? CaptureToRectArea();
        var bloodLower = new Scalar(255, 90, 90); // BGR 红色

        using var cropped = image.DeriveCrop(0, 0, 1500, 900);
        using Mat mask = OpenCvCommonHelper.Threshold(
            cropped.SrcMat, bloodLower);

        using Mat labels = new Mat();
        using Mat stats = new Mat();
        using Mat centroids = new Mat();

        int numLabels = Cv2.ConnectedComponentsWithStats(
            mask, labels, stats, centroids,
            connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

        for (int i = 1; i < numLabels; i++)
        {
            using Mat row = stats.Row(i);
            if (row.GetArray(out int[] arr))
            {
                int x = arr[0], y = arr[1], width = arr[2], height = arr[3];
                if (y < 50)
                    continue;
                results.Add((x, y, width, height));
            }
        }

        // 自动更新传奇血条动态追踪
        UpdateLegendaryBarTracker(results.Select(r => r.y));

        return results;
    }

    /// <summary>
    /// 根据配置的伤害数字识别模式寻找伤害数字/反应文字。
    ///   - Disabled：直接返回 null
    ///   - Ocr：使用 OCR 识别
    ///   - Color：使用颜色分析识别
    /// </summary>
    public static (int centerX, int centerY, string text, int x, int y, int width, int height)? FindDamageNumber(ImageRegion? existingCapture = null)
    {
        var mode = TaskContext.Instance().Config.AutoFightConfig.DamageNumberRecognitionMode;
        switch (mode)
        {
            case DamageNumberRecognitionMode.Disabled:
                return null;
            case DamageNumberRecognitionMode.Color:
                return FindDamageNumberByColor(existingCapture);
            case DamageNumberRecognitionMode.Ocr:
            default:
                return FindDamageNumberByOcr(existingCapture);
        }
    }

    /// <summary>
    /// OCR 寻找伤害数字/反应文字作为追踪目标（备用寻敌）。
    /// 在 450,240-1600,900 区域 OCR，过滤条件：
    ///   - 有效项1：排除首位 '+'，去除非数字后纯数字 ≥4 位
    ///   - 有效项2：文本包含反应关键词（免疫/蒸发/感电/结晶/扩散/绽放/冻结/超载/融化/燃烧/超导/激化），跳过数字过滤
    /// 按 h²×文本字数 加权得到中心坐标，返回离加权中心最近的有效项。
    /// </summary>
    private static (int centerX, int centerY, string text, int x, int y, int width, int height)? FindDamageNumberByOcr(ImageRegion? existingCapture = null)
    {
        using var ra = existingCapture ?? CaptureToRectArea();
        var ocrResults = ra.FindMulti(RecognitionObject.Ocr(450, 240, 1150, 660));

        string[] reactionKeywords = ["免疫", "蒸发", "感电", "结晶", "扩散", "绽放", "冻结", "超载", "融化", "燃烧", "超导", "激化"];
        var validItems = new List<(int cx, int cy, int area, string text, int x, int y, int w, int h)>();

        foreach (var r in ocrResults)
        {
            var text = r.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            // 有效项2：反应关键词（跳过所有过滤）
            if (reactionKeywords.Any(k => text.Contains(k)))
            {
                validItems.Add((r.X + r.Width / 2, r.Y + r.Height / 2, r.Height * r.Height * text.Length, text, r.X, r.Y, r.Width, r.Height));
                continue;
            }

            // 有效项1：排除 '+' 开头
            if (text[0] == '+') continue;

            // 去除非数字，纯数字 ≥4 位
            var digits = new string(text.Where(char.IsDigit).ToArray());
            if (digits.Length >= 4)
            {
                validItems.Add((r.X + r.Width / 2, r.Y + r.Height / 2, r.Height * r.Height * text.Length, text, r.X, r.Y, r.Width, r.Height));
            }
        }

        if (validItems.Count == 0) return null;

        int totalArea = validItems.Sum(i => i.area);
        if (totalArea == 0) return null;

        double avgX = (double)validItems.Sum(i => i.cx * i.area) / totalArea;
        double avgY = (double)validItems.Sum(i => i.cy * i.area) / totalArea;

        var closest = validItems.OrderBy(i => Math.Abs(i.cx - avgX) + Math.Abs(i.cy - avgY)).First();

        return (closest.cx, closest.cy, closest.text, closest.x, closest.y, closest.w, closest.h);
    }

    /// <summary>
    /// 颜色分析模式：在 450,240-1600,900 区域内查找固定颜色的像素，
    /// 经连通域分析后舍弃高度小于20的区域，返回加权中心。
    /// </summary>
    private static (int centerX, int centerY, string text, int x, int y, int width, int height)? FindDamageNumberByColor(ImageRegion? existingCapture = null)
    {
        using var ra = existingCapture ?? CaptureToRectArea();

        // 目标颜色 (RGB)
        Scalar[] targetColors =
        [
            new(225, 155, 255), // 雷 #E19BFF
            new(153, 255, 255), // 冰 #99FFFF
            new(51, 204, 255),  // 水 #33CCFF
            new(102, 255, 204), // 风 #66FFCC
            new(255, 155, 0),   // 火 #FF9B00
            new(0, 234, 82),    // 草 #00EA52
            new(255, 204, 102), // 岩 #FFCC66
        ];

        const int roiX = 450;
        const int roiY = 240;
        const int roiW = 1150;
        const int roiH = 660;

        using var cropped = ra.DeriveCrop(roiX, roiY, roiW, roiH);
        using var rgbMat = new Mat();
        Cv2.CvtColor(cropped.SrcMat, rgbMat, ColorConversionCodes.BGR2RGB);

        using var combinedMask = new Mat(cropped.SrcMat.Size(), MatType.CV_8UC1, Scalar.All(0));

        foreach (var color in targetColors)
        {
            using var mask = new Mat();
            Cv2.InRange(rgbMat, color, color, mask);
            Cv2.BitwiseOr(combinedMask, mask, combinedMask);
        }

        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        var numLabels = Cv2.ConnectedComponentsWithStats(combinedMask, labels, stats, centroids,
            connectivity: PixelConnectivity.Connectivity4, ltype: MatType.CV_32S);

        if (numLabels <= 1) return null;

        var validItems = new List<(int cx, int cy, int area, int x, int y, int w, int h)>();
        for (int i = 1; i < numLabels; i++)
        {
            int x = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
            int y = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
            int width = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            int height = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);

            if (height < 20) continue;

            int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            validItems.Add((x + width / 2 + roiX, y + height / 2 + roiY, area, x + roiX, y + roiY, width, height));
        }

        if (validItems.Count == 0) return null;

        int totalArea = validItems.Sum(i => i.area);
        if (totalArea == 0) return null;

        double avgX = (double)validItems.Sum(i => i.cx * i.area) / totalArea;
        double avgY = (double)validItems.Sum(i => i.cy * i.area) / totalArea;

        var closest = validItems.OrderBy(i => Math.Abs(i.cx - avgX) + Math.Abs(i.cy - avgY)).First();

        return (closest.cx, closest.cy, "", closest.x, closest.y, closest.w, closest.h);
    }
}
