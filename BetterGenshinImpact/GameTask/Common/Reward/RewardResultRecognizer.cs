using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.GameUI;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Common.Reward;

/// <summary>
/// 奖励结果页识别器。
/// </summary>
public class RewardResultRecognizer
{
    private static readonly Lazy<RewardResultRecognizer> _instance = new(() => new RewardResultRecognizer());
    public static RewardResultRecognizer Instance => _instance.Value;

    private readonly ILogger<RewardResultRecognizer> _logger = App.GetLogger<RewardResultRecognizer>();
    private static readonly Scalar RewardMaskLower = new(0, 0, 190);
    private static readonly Scalar RewardMaskUpper = new(179, 20, 249);

    /// <summary>
    /// 单张奖励卡片的识别中间结果。
    /// </summary>
    /// <param name="Name">奖励名称。</param>
    /// <param name="Count">奖励数量；未知时为 -1。</param>
    /// <param name="QualityLevel">稀有度；未知或不支持时为 -1。</param>
    /// <param name="CardIndex">卡片在奖励条带中的下标；仅用于调试截图把识别结果映射回卡片矩形。</param>
    private sealed record RecognizedReward(string? Name, int Count, int QualityLevel = -1, int CardIndex = -1);

    /// <summary>
    /// 单页奖励识别结果。
    /// </summary>
    /// <param name="Rewards">本页奖励中间结果。</param>
    /// <param name="CardRects">本页卡片矩形。</param>
    private sealed record RewardPageRecognitionResult(List<RecognizedReward> Rewards, List<Rect> CardRects);

    private RewardResultRecognizer()
    {
    }

    /// <summary>
    /// 配置根。单例没有创建方可以传参，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;

    /// <summary>
    /// 识别多页奖励卡片。
    /// </summary>
    /// <param name="runtime">本次识别使用的运行环境，截图与鼠标移动都只使用它。</param>
    /// <param name="maxPages">最大识别页数。</param>
    /// <returns>奖励名称到总数量的映射。</returns>
    public Dictionary<string, int> RecognizeMultiPage(GameRuntime runtime, int maxPages = 3)
    {
        if (!IsSupportedRewardResolution(runtime))
        {
            return new Dictionary<string, int>();
        }

        // 本轮累计识别到的新奖励。
        List<RewardItem> allRewards = [];
        // 上一页原始奖励，用于判断翻页后的重复卡片。
        List<RewardItem>? previousPageRewards = null;

        // 整轮识别共用一个识别器实例，用完销毁，不常驻内存。
        using IItemIconRecognizer iconRecognizer = ItemIconRecognizerFactory.CreateConfigured();

        // 调试截图共用的时间戳槽位；实际值在首次保存时由 debug 方法生成。
        string? rewardDebugRunId = null;

        for (int currentPage = 1; currentPage <= maxPages; currentPage++)
        {
            if (currentPage > 1)
            {
                PerformDragToNextPage(runtime);
            }

            using var screen = CaptureRewardPageScreen(runtime);
            var pageResult = RecognizeRewardPage(screen, iconRecognizer);
            SaveRewardDebugImage(currentPage, screen.SrcMat, pageResult, ref rewardDebugRunId);

            if (pageResult.Rewards.Count == 0)
            {
                _logger.LogWarning("奖励识别：没有识别到奖励，已结束本次识别。");
                break;
            }

            var currentPageRewards = ToRewardItems(pageResult.Rewards);
            int duplicateCount = previousPageRewards is { Count: > 0 }
                ? DetectDuplicates(currentPageRewards, previousPageRewards)
                : 0;
            var newRewards = duplicateCount > 0
                ? currentPageRewards.Skip(duplicateCount).ToList()
                : currentPageRewards;

            if (newRewards.Count > 0)
            {
                allRewards.AddRange(newRewards);
            }

            if (duplicateCount > 0)
            {
                break;
            }

            previousPageRewards = currentPageRewards;
        }

        _logger.LogInformation("奖励识别：本次共识别到 {TotalCount} 个奖励。", allRewards.Count);
        return CreateSummary(allRewards);
    }

    /// <summary>
    /// 移开鼠标并截取当前游戏画面。
    /// </summary>
    /// <param name="runtime">运行环境。</param>
    /// <returns>当前游戏画面截图。</returns>
    private static ImageRegion CaptureRewardPageScreen(GameRuntime runtime)
    {
        runtime.GameRegion1080PPosMove(960, 720);
        Thread.Sleep(100);
        return runtime.CaptureToRectArea();
    }

    /// <summary>
    /// 截取一张画面并检查奖励识别支持的分辨率。
    /// </summary>
    /// <param name="runtime">运行环境。</param>
    /// <returns>支持时返回 true。</returns>
    private bool IsSupportedRewardResolution(GameRuntime runtime)
    {
        using var screen = CaptureRewardPageScreen(runtime);
        if (screen.SrcMat.Width == 1920 && screen.SrcMat.Height == 1080)
        {
            return true;
        }

        _logger.LogWarning("奖励识别：当前分辨率 {Width}x{Height} 暂不支持。", screen.SrcMat.Width, screen.SrcMat.Height);
        return false;
    }

    /// <summary>
    /// 识别单页奖励截图。
    /// </summary>
    /// <param name="screen">当前页全屏截图。</param>
    /// <param name="iconRecognizer">物品图标识别器。</param>
    /// <returns>本页奖励与卡片位置。</returns>
    private RewardPageRecognitionResult RecognizeRewardPage(ImageRegion screen, IItemIconRecognizer iconRecognizer)
    {
        using var bandMat = new Mat(screen.SrcMat, new Rect(220, 444, 1480, 220));
        var cardRects = DetectCardRects(bandMat);
        var recognizedRewards = RecognizeRewards(bandMat, cardRects, iconRecognizer);
        return new RewardPageRecognitionResult(recognizedRewards, cardRects);
    }

    /// <summary>
    /// 将内部识别结果转换为奖励物品。
    /// </summary>
    /// <param name="rewards">内部识别结果。</param>
    /// <returns>可汇总与去重的奖励物品列表。</returns>
    private static List<RewardItem> ToRewardItems(List<RecognizedReward> rewards)
    {
        return rewards
            .Where(r => !string.IsNullOrEmpty(r.Name))
            .Select((r, index) => new RewardItem(
                r.Name!,
                r.QualityLevel,
                r.Count >= 0 ? r.Count : 1,
                index))
            .ToList();
    }

    /// <summary>
    /// 汇总奖励名称到总数量。
    /// </summary>
    /// <param name="rewards">奖励物品列表。</param>
    /// <returns>奖励名称到总数量的映射。</returns>
    private static Dictionary<string, int> CreateSummary(IEnumerable<RewardItem> rewards)
    {
        return rewards
            .GroupBy(r => r.Name)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
    }

    /// <summary>
    /// 保存带名称与数量标注的奖励检测图；仅用于调试截图。
    /// </summary>
    /// <param name="page">当前页码。</param>
    /// <param name="screenMat">全屏截图。</param>
    /// <param name="pageResult">本页奖励识别结果与卡片矩形。</param>
    /// <param name="rewardDebugRunId">本轮识别共用的时间戳；首次保存时生成。</param>
    private void SaveRewardDebugImage(int page, Mat screenMat, RewardPageRecognitionResult pageResult, ref string? rewardDebugRunId)
    {
        var commonConfig = Config.CommonConfig;
        if (!commonConfig.ScreenshotEnabled || !commonConfig.RewardRecognitionScreenshotEnabled)
        {
            return;
        }

        rewardDebugRunId ??= DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");

        try
        {
            var directory = Global.Absolute(@"log\RewardRecognition");
            Directory.CreateDirectory(directory);

            // 调试图顶部预留文字区，避免名称超出条带顶部被截断。
            const int labelHeight = 22;
            const int labelTopPadding = labelHeight + 4;

            using var bandMat = new Mat(screenMat, new Rect(220, 444, 1480, 220));
            using var debugMat = new Mat();
            Cv2.CopyMakeBorder(bandMat, debugMat, labelTopPadding, 0, 0, 0, BorderTypes.Constant, Scalar.Black);

            foreach (var rect in pageResult.CardRects)
            {
                // 检测框向外扩 1px，避免边框压住图标边缘。
                var debugRect = new Rect(rect.X - 1, rect.Y + labelTopPadding - 1, rect.Width + 2, rect.Height + 2);
                Cv2.Rectangle(debugMat, debugRect, Scalar.LimeGreen, 2);
            }

            using var bitmap = debugMat.ToBitmap();
            using var graphics = Graphics.FromImage(bitmap);
            using var font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
            using var backgroundBrush = new SolidBrush(Color.FromArgb(235, 0, 0, 0));
            using var successBrush = new SolidBrush(Color.LimeGreen);
            using var failedBrush = new SolidBrush(Color.Red);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
            };

            var rewardsByCard = pageResult.Rewards.ToDictionary(r => r.CardIndex);

            for (int cardIdx = 0; cardIdx < pageResult.CardRects.Count; cardIdx++)
            {
                // 图标识别失败的卡片没有识别结果，单独标出，避免调试时误以为漏画。
                string label;
                Brush brush;
                if (rewardsByCard.TryGetValue(cardIdx, out var reward))
                {
                    if (reward.Count >= 0)
                    {
                        label = $"{reward.Name} x{reward.Count}";
                        brush = successBrush;
                    }
                    else
                    {
                        label = $"{reward.Name} x?";
                        brush = failedBrush;
                    }
                }
                else
                {
                    label = "名称未识别";
                    brush = failedBrush;
                }

                var cardRect = pageResult.CardRects[cardIdx];
                // 名称画在卡片上方，底边与绿框保留 1px 间隙。
                var labelRect = new RectangleF(cardRect.X + 2, cardRect.Y + labelTopPadding - labelHeight - 2, cardRect.Width - 4, labelHeight);
                graphics.FillRectangle(backgroundBrush, labelRect);
                graphics.DrawString(label, font, brush, labelRect, format);
            }

            bitmap.Save(Path.Combine(directory, $"rewardRecDebug_{rewardDebugRunId}_{page}.png"), ImageFormat.Png);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "奖励识别：调试截图保存失败，不影响本次识别。");
        }
    }

    /// <summary>
    /// 检测当前页开头与上一页结尾重复的奖励数量。
    /// </summary>
    /// <param name="currentPage">当前页奖励。</param>
    /// <param name="previousPage">上一页奖励。</param>
    /// <returns>重复奖励数量。</returns>
    private static int DetectDuplicates(List<RewardItem> currentPage, List<RewardItem> previousPage)
    {
        int duplicateCount = 0;

        // 从当前页的第一个开始，依次与上一页的后面部分比对
        for (int i = 0; i < currentPage.Count && i < previousPage.Count; i++)
        {
            bool foundMatch = false;

            // 检查当前页第i个是否与上一页某个匹配
            for (int j = Math.Max(0, previousPage.Count - 10); j < previousPage.Count; j++)
            {
                if (IsSameRewardCard(currentPage[i], previousPage[j]))
                {
                    foundMatch = true;
                    duplicateCount++;
                    break;
                }
            }

            // 如果当前位置没有匹配，说明重复序列已经结束
            if (!foundMatch)
            {
                break;
            }
        }

        return duplicateCount;
    }

    /// <summary>
    /// 判断两张奖励卡片是否为翻页重叠的同一卡片。
    /// </summary>
    /// <param name="current">当前页卡片。</param>
    /// <param name="previous">上一页卡片。</param>
    /// <returns>名称、稀有度和数量都一致时返回 true。</returns>
    private static bool IsSameRewardCard(RewardItem current, RewardItem previous)
    {
        return current.Name == previous.Name
               && current.QualityLevel == previous.QualityLevel
               && current.Quantity == previous.Quantity;
    }

    /// <summary>
    /// 拖动奖励列表到下一页。
    /// </summary>
    /// <param name="runtime">运行环境。</param>
    private void PerformDragToNextPage(GameRuntime runtime)
    {
        const int startX = 1650;
        const int endX = 250;
        const int y = 540;

        runtime.GameRegion1080PPosMove(startX, y);
        Thread.Sleep(100);

        InputHub.Foreground.Mouse.LeftButtonDown();
        Thread.Sleep(100);

        int steps = 20;
        for (int step = 0; step <= steps; step++)
        {
            double currentX = startX + (endX - startX) * step / (double)steps;
            runtime.GameRegion1080PPosMove(currentX, y);
            Thread.Sleep(30);
        }

        InputHub.Foreground.Mouse.LeftButtonUp();

        // 等待末页不足10个奖励时的回退动画完成。
        Thread.Sleep(1200);
    }

    /// <summary>
    /// 合并一次奖励识别汇总到累计汇总。
    /// </summary>
    /// <param name="summary">累计汇总。</param>
    /// <param name="recognizedSummary">本次识别汇总。</param>
    public static void MergeIntoSummary(IDictionary<string, int> summary, IDictionary<string, int> recognizedSummary)
    {
        foreach (var pair in recognizedSummary)
        {
            if (pair.Value >= 0)
            {
                summary[pair.Key] = summary.TryGetValue(pair.Key, out var current) && current >= 0
                    ? current + pair.Value
                    : pair.Value;
            }
            else if (!summary.ContainsKey(pair.Key))
            {
                summary[pair.Key] = -1;
            }
        }
    }

    /// <summary>
    /// 识别奖励条带中的奖励卡片。
    /// </summary>
    /// <param name="bandMat">奖励条带。</param>
    /// <param name="cardRects">已定位的卡片矩形。</param>
    /// <param name="iconRecognizer">物品图标识别器。</param>
    /// <param name="ocrService">OCR 服务，为空时使用 Paddle OCR。</param>
    private List<RecognizedReward> RecognizeRewards(Mat bandMat, List<Rect> cardRects, IItemIconRecognizer iconRecognizer, IOcrService? ocrService = null)
    {
        ocrService ??= OcrFactory.Paddle;

        if (cardRects.Count == 0)
        {
            _logger.LogDebug("奖励识别：未定位到奖励卡片");
            return new List<RecognizedReward>();
        }

        _logger.LogDebug("奖励识别：卡片数量 {Count}", cardRects.Count);

        var results = new List<RecognizedReward>();

        for (int cardIdx = 0; cardIdx < cardRects.Count; cardIdx++)
        {
            // === 裁剪卡片区域 ===
            var cardRect = cardRects[cardIdx];
            using var cardMat = new Mat(bandMat, cardRect);

            // === 图标识别===
            var iconName = RecognizeIcon(iconRecognizer, cardMat, cardIdx);
            if (iconName == null)
            {
                _logger.LogWarning("奖励识别：已跳过一个未识别的奖励图标");
                continue;
            }

            // === 数量 OCR ===
            var count = RecognizeCountByOcr(cardMat, ocrService, cardIdx);

            results.Add(new RecognizedReward(iconName, count, CardIndex: cardIdx));
        }

        return results;
    }

    /// <summary>
    /// 识别单张奖励卡片图标的名称。
    /// </summary>
    /// <param name="iconRecognizer">物品图标识别器。</param>
    /// <param name="cardMat">奖励卡片图像。</param>
    /// <param name="cardIdx">卡片序号。</param>
    /// <returns>奖励名称；未识别时返回 null。</returns>
    private string? RecognizeIcon(IItemIconRecognizer iconRecognizer, Mat cardMat, int cardIdx)
    {
        try
        {
            using Mat icon = cardMat.GetGridIcon(); // 归一化为 125×125
            return iconRecognizer.Recognize(icon);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "奖励识别：卡片 {CardIndex} 图标识别异常，已忽略", cardIdx);
            return null;
        }
    }

    /// <summary>
    /// 通过 OCR 识别单张奖励卡片数量。
    /// </summary>
    /// <param name="cardMat">奖励卡片图像。</param>
    /// <param name="ocrService">OCR 服务。</param>
    /// <param name="cardIdx">卡片序号。</param>
    /// <returns>识别到的数量；失败时返回 -1。</returns>
    private int RecognizeCountByOcr(Mat cardMat, IOcrService ocrService, int cardIdx)
    {
        try
        {
            using GridItemCountRecognitionResult result =
                GridItemCountRecognizer.RecognizeCropped(cardMat, ocrService);
            if (result.Count >= 0)
            {
                // NARROW_ONE / WIDE_SEVEN 为结构纠错成功，记录原文与纠正结果便于排查。
                if (result.Reason != null)
                {
                    _logger.LogDebug(
                        "奖励识别：卡片 {CardIndex} 数量结构纠错：{Reason}，OCR 原文：{RawText}，有效连通域：{ComponentCount}，纠正结果：{Count}",
                        cardIdx, result.Reason, result.RawText, result.ComponentCount, result.Count);
                }

                return result.Count;
            }

            _logger.LogWarning(
                "奖励识别：卡片 {CardIndex} 数量没有识别出来，OCR 原文：{RawText}，原因：{Reason}，有效连通域：{ComponentCount}，已按 1 个计入。",
                cardIdx,
                result.RawText,
                result.Reason,
                result.ComponentCount);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "奖励识别：卡片 {CardIndex} 数量OCR异常，数量保持 -1", cardIdx);
            _logger.LogWarning("奖励识别：卡片 {CardIndex} 数量识别异常，已按 1 个计入。", cardIdx);
        }

        return -1;
    }

    /// <summary>
    /// 定位奖励条带中的卡片矩形。
    /// </summary>
    /// <param name="bandMat">奖励条带图。</param>
    /// <returns>按 X 升序排列的卡片矩形。</returns>
    internal static List<Rect> DetectCardRects(Mat bandMat)
    {
        // 1080P 基准卡片尺寸：图标 125x125 + 底部数量条带。
        const int cardW = 125;
        const int cardH = 153;

        // 条带比例用于连通区域筛选。
        const double stripMinWidthRatio = 0.55;
        const double stripMaxWidthRatio = 1.35;
        const double stripMinHeightRatio = 0.10;
        const double stripMaxHeightRatio = 0.50;
        const int stripMinArea = 1200;
        const double stripMinCenterYRatio = 0.40;

        // HSV 阈值得到浅色条带掩码
        using var mask = CreateRewardBandMask(bandMat);

        // 连通区域分析
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();
        int nLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids,
            connectivity: PixelConnectivity.Connectivity8, ltype: MatType.CV_32S);

        double minWidth = stripMinWidthRatio * cardW;
        double maxWidth = stripMaxWidthRatio * cardW;
        double minHeight = stripMinHeightRatio * cardH;
        double maxHeight = stripMaxHeightRatio * cardH;
        int minArea = stripMinArea;
        double minCenterY = stripMinCenterYRatio * bandMat.Height;

        var strips = new List<Rect>();
        for (int i = 1; i < nLabels; i++) // 跳过编号 0（背景）
        {
            using var rowMat = stats.Row(i);
            if (!rowMat.GetArray<int>(out var row))
            {
                continue;
            }

            int x = row[0], y = row[1], w = row[2], h = row[3], area = row[4];
            if (w < minWidth || w > maxWidth)
            {
                continue;
            }

            if (h < minHeight || h > maxHeight)
            {
                continue;
            }

            if (area < minArea)
            {
                continue;
            }

            if (y + h / 2.0 < minCenterY)
            {
                continue; // 排除上方建筑/标题等高亮噪点
            }

            strips.Add(new Rect(x, y, w, h));
        }

        if (strips.Count == 0)
        {
            return new List<Rect>();
        }

        // 单行约束：仅保留与中位 Y 接近的条带，排除偶发的异常 Y 噪点
        strips.Sort((a, b) => (a.Y + a.Height).CompareTo(b.Y + b.Height));
        int medianBottom = strips[strips.Count / 2].Y + strips[strips.Count / 2].Height;
        double yTolerance = cardH * 0.5;
        strips = strips.Where(s => Math.Abs(s.Y + s.Height - medianBottom) <= yTolerance).ToList();

        strips.Sort((a, b) => (a.Y + a.Height).CompareTo(b.Y + b.Height));
        int rowBottom = strips[strips.Count / 2].Y + strips[strips.Count / 2].Height;

        // 由条带反推卡片矩形：水平居中对齐，底边统一对齐到本行条带底边中位数。
        // 单个条带可能被数量白底、名称白字或闪光点连通拉高，不能用它自己的底边反推卡片 Y 坐标。
        var cardRects = new List<Rect>();
        foreach (var strip in strips)
        {
            int centerX = strip.X + strip.Width / 2;
            int cardX = centerX - cardW / 2;
            int cardY = rowBottom - cardH;
            var cardRect = new Rect(cardX, cardY, cardW, cardH);

            // 越界则跳过（保持完整 125×153 比例，避免破坏 GetGridIcon/GetGridItemIconText 的内部比例）
            if (cardX < 0 || cardY < 0 || cardX + cardW > bandMat.Width || cardY + cardH > bandMat.Height)
            {
                continue;
            }

            cardRects.Add(cardRect);
        }

        cardRects.Sort((a, b) => a.X.CompareTo(b.X));
        return cardRects;
    }

    private static Mat CreateRewardBandMask(Mat bandMat)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(bandMat, hsv, ColorConversionCodes.BGR2HSV);
        var mask = new Mat();
        Cv2.InRange(hsv, RewardMaskLower, RewardMaskUpper, mask);
        using var kernel = Mat.Ones(3, 3, MatType.CV_8UC1);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        return mask;
    }
}
