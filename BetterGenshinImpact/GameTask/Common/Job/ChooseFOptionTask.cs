using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Assets;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 在大世界的多个 F 交互选项中，通过滚轮选择指定文本。
/// 调用方应保证任务期间没有其他任务或用户同时发送游戏输入。
/// </summary>
public class ChooseFOptionTask
{
    private const int RecognitionRetryCount = 5;
    private const int RetryMouseMoveDistance = 400;
    private static readonly SemaphoreSlim SelectionLock = new(1, 1);
    private readonly ILogger<ChooseFOptionTask> _logger = App.GetLogger<ChooseFOptionTask>();

    public string Name => "选择指定 F 交互选项";

    /// <summary>
    /// 识别 F 周围的可见候选项，按 Y 坐标从上到下返回。
    /// 返回值只包含截图坐标和文本，不持有截图，也不用于鼠标点击。
    /// </summary>
    public List<Region> RecognizeOption(ImageRegion region, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(region);
        ct.ThrowIfCancellationRequested();
        var assets = AutoPickAssets.Get(region, TaskContext.Instance().Config.AutoPickConfig.PickKey);
        using var pick = region.Find(assets.PickRo);
        if (pick.IsEmpty())
        {
            _logger.LogWarning("F选项识别：未找到交互键");
            return [];
        }

        return RecognizeOption(region, pick, ct);
    }

    /// <summary>
    /// 精确匹配目标文本；必要时按候选项序号差滚动，重新 OCR 确认后才按交互键。
    /// 返回 true 表示已确认目标文本并发送按键，不代表后续界面已成功打开。
    /// 未找到目标、定位有歧义或复核失败时返回 false，不按交互键。
    /// </summary>
    public async Task<bool> SingleSelectText(string option, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(option);
        var expected = NormalizeText(option);
        var runner = RunnerContext.Instance;
        await SelectionLock.WaitAsync(ct);
        try
        {
            runner.StopAutoPick();
            try
            {
                _logger.LogInformation("F选项选择：目标为 {Text}", option);
                await Delay(100, ct);
                int clicks;
                var taskContext = TaskContext.Instance();
                var dpiScale = float.IsFinite(taskContext.DpiScale) && taskContext.DpiScale > 0
                    ? taskContext.DpiScale
                    : 1f;
                // MoveMouseBy 使用物理移动量，这里的 400 是逻辑距离，需要按当前 DPI 缩放。
                var retryMouseMoveDistance = Math.Max(1, (int)Math.Round(RetryMouseMoveDistance * dpiScale));
                for (var completedRetryCount = 0;; completedRetryCount++)
                {
                    using (var region = CaptureToRectArea())
                    {
                        var assets = AutoPickAssets.Get(region, taskContext.Config.AutoPickConfig.PickKey);
                        using var pick = region.Find(assets.PickRo);
                        if (pick.IsEmpty())
                        {
                            _logger.LogWarning("F选项选择：未找到交互键，停止选择 {Text}", option);
                            return false;
                        }

                        var current = RecognizeCurrentOption(region, pick, ct);
                        _logger.LogInformation("F选项选择：当前文本 {Current}，目标文本 {Expected}", current, expected);
                        if (current == expected)
                        {
                            ct.ThrowIfCancellationRequested();
                            await Delay(60, ct);
                            Simulation.SendInput.Keyboard.KeyDown(assets.PickVk);
                            try
                            {
                                await Delay(60, ct);
                            }
                            finally
                            {
                                Simulation.SendInput.Keyboard.KeyUp(assets.PickVk);
                            }
                            await Delay(60, ct);
                            _logger.LogInformation("F选项选择：已确认并按下 {Key}，选项 {Text}", assets.PickVk, current);
                            return true;
                        }

                        var candidates = RecognizeOption(region, pick, ct);
                        var scrollClicks = GetScrollClicks(candidates, pick.ToRect(), expected);
                        if (scrollClicks.HasValue && scrollClicks.Value != 0)
                        {
                            clicks = scrollClicks.Value;
                            break;
                        }

                        if (completedRetryCount >= RecognitionRetryCount)
                        {
                            _logger.LogWarning(
                                "F选项选择：重试 {RetryCount} 次后，目标不存在、位置有歧义或当前行文本不一致，取消选择 {Text}",
                                RecognitionRetryCount, option);
                            return false;
                        }

                        _logger.LogWarning(
                            "F选项选择：目标不存在、位置有歧义或当前行文本不一致，鼠标向右移动 {Distance} 后进行第 {RetryIndex}/{RetryCount} 次重试，目标 {Text}",
                            retryMouseMoveDistance, completedRetryCount + 1, RecognitionRetryCount, option);
                    }

                    ct.ThrowIfCancellationRequested();
                    await Delay(60, ct);
                    Simulation.SendInput.Mouse.MoveMouseBy(retryMouseMoveDistance, 0);
                    await Delay(60, ct);
                    // 游戏处理输入和截图源刷新都存在延迟，等待稳定后再进行下一次识别。
                    await Delay(200, ct);
                }

                _logger.LogInformation("F选项选择：向{Direction}滚动 {Count} 格，目标 {Text}",
                    clicks > 0 ? "上" : "下", Math.Abs(clicks), option);
                for (var i = 0; i < Math.Abs(clicks); i++)
                {
                    ct.ThrowIfCancellationRequested();
                    // 每一次滚轮输入都保留前后间隔，避免低帧率下连续输入丢失。
                    await Delay(60, ct);
                    Simulation.SendInput.Mouse.VerticalScroll(Math.Sign(clicks));
                    await Delay(60, ct);
                }

                await Delay(200, ct);
                using var confirmation = CaptureToRectArea();
                var confirmationAssets = AutoPickAssets.Get(confirmation, TaskContext.Instance().Config.AutoPickConfig.PickKey);
                using var confirmationPick = confirmation.Find(confirmationAssets.PickRo);
                if (confirmationPick.IsEmpty())
                {
                    _logger.LogWarning("F选项选择：滚动后交互键消失，不发送按键");
                    return false;
                }

                var confirmedText = RecognizeCurrentOption(confirmation, confirmationPick, ct);
                if (confirmedText != expected)
                {
                    _logger.LogWarning("F选项选择：滚动后复核失败，当前 {Current}，期望 {Expected}，不发送按键",
                        confirmedText, expected);
                    return false;
                }

                ct.ThrowIfCancellationRequested();
                await Delay(60, ct);
                Simulation.SendInput.Keyboard.KeyDown(confirmationAssets.PickVk);
                try
                {
                    await Delay(60, ct);
                }
                finally
                {
                    Simulation.SendInput.Keyboard.KeyUp(confirmationAssets.PickVk);
                }
                await Delay(60, ct);
                _logger.LogInformation("F选项选择：复核通过，已按下 {Key}，选项 {Text}", confirmationAssets.PickVk, confirmedText);
                return true;
            }
            finally
            {
                runner.ResumeAutoPick();
            }
        }
        finally
        {
            SelectionLock.Release();
        }
    }

    private List<Region> RecognizeOption(ImageRegion region, Region pick, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var config = TaskContext.Instance().Config.AutoPickConfig;
        var rect = GetCandidateRect(CaptureSize.From(region), pick.ToRect(),
            config.ItemTextLeftOffset, config.ItemTextRightOffset);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            _logger.LogWarning("F选项识别：候选区域无效 {Rect}", rect);
            return [];
        }

        var results = region.FindMulti(RecognitionObject.Ocr(rect));
        try
        {
            ct.ThrowIfCancellationRequested();
            var candidates = MergeCandidateRows(results);
            _logger.LogInformation("F选项识别：找到 {Count} 个候选项：{Options}",candidates.Count,
                string.Join("；", candidates.Select((item, index) => $"{index + 1}. {item.Text} (Y={item.Y})")));
            return candidates;
        }
        finally
        {
            foreach (var result in results)
            {
                result.Dispose();
            }
        }
    }

    private string RecognizeCurrentOption(ImageRegion region, Region pick, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var config = TaskContext.Instance().Config.AutoPickConfig;
        var scale = CaptureSize.From(region).AssetScale;
        var rect = new Rect(pick.X + (int)(config.ItemTextLeftOffset * scale), pick.Y,
            (int)((config.ItemTextRightOffset - config.ItemTextLeftOffset) * scale), pick.Height);
        // 当前行必须完整位于截图内，不能拿裁剪后的残缺文本确认交互。
        if (rect.Width <= 0 || rect.Height <= 0 || rect.X < 0 || rect.Y < 0
            || rect.Right > region.Width || rect.Bottom > region.Height)
        {
            _logger.LogWarning("F选项识别：当前选项文字区域越界 {Rect}", rect);
            return string.Empty;
        }

        using var gradient = new Mat(region.CacheGreyMat, new Rect(rect.X, rect.Y, rect.Width, Math.Min(rect.Height, 3)));
        using var sobel = gradient.Sobel(MatType.CV_32F, 1, 0);
        if (sobel.Mean().Val0 < -3)
        {
            _logger.LogWarning("F选项识别：当前行处于拾取动画中，跳过确认");
            return string.Empty;
        }

        string text;
        using (var textMat = new Mat(region.SrcMat, rect))
        {
            var bounds = TextRectExtractor.GetTextBoundingRect(textMat);
            if (bounds.X < 20 && bounds.Width > 5 && bounds.Height > 5)
            {
                using var textOnly = new Mat(textMat, new Rect(0, 0, Math.Min(bounds.Right + 5, textMat.Width), textMat.Height));
                text = OcrFactory.Paddle.OcrWithoutDetector(textOnly);
            }
            else
            {
                text = OcrFactory.Paddle.Ocr(textMat);
            }
        }

        ct.ThrowIfCancellationRequested();
        return NormalizeText(text);
    }

    internal static Rect GetCandidateRect(CaptureSize size, Rect pick, int leftOffset, int rightOffset)
    {
        var scale = size.AssetScale;
        var textLeft = pick.X + (int)(leftOffset * scale);
        var left = Math.Clamp(textLeft, 0, size.Width);
        var right = Math.Clamp(textLeft + (int)((rightOffset - leftOffset) * scale), 0, size.Width);
        var top = Math.Clamp(pick.Top - (int)(500 * scale), 0, size.Height);
        var bottom = Math.Clamp(size.Height - (int)(220 * scale), 0, size.Height);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    internal static List<Region> MergeCandidateRows(IEnumerable<Region> results)
    {
        var rows = new List<List<Region>>();
        foreach (var item in results.Where(r => r.Width > 0 && r.Height > 0 && NormalizeText(r.Text).Length > 0)
                     .OrderBy(r => r.Y).ThenBy(r => r.X))
        {
            // 同一行的多个 OCR 框只算一个候选项，避免滚轮步数被文字碎片放大。
            var row = rows.FirstOrDefault(group => group.Any(other =>
                Math.Min(other.Bottom, item.Bottom) - Math.Max(other.Top, item.Top)
                > Math.Min(other.Height, item.Height) / 2d));
            if (row is null)
            {
                rows.Add([item]);
            }
            else
            {
                row.Add(item);
            }
        }

        return rows.Select(row => new Region
        {
            X = row.Min(r => r.X),
            Y = row.Min(r => r.Y),
            Width = row.Max(r => r.Right) - row.Min(r => r.X),
            Height = row.Max(r => r.Bottom) - row.Min(r => r.Y),
            Text = NormalizeText(string.Concat(row.OrderBy(r => r.X).Select(r => r.Text)))
        }).OrderBy(r => r.Y).ToList();
    }

    internal static int? GetScrollClicks(IReadOnlyList<Region> candidates, Rect pick, string expected)
    {
        var targetIndices = Enumerable.Range(0, candidates.Count)
            .Where(i => NormalizeText(candidates[i].Text) == NormalizeText(expected)).ToList();
        var currentIndices = Enumerable.Range(0, candidates.Count)
            .Where(i => candidates[i].Top < pick.Bottom && candidates[i].Bottom > pick.Top).ToList();
        if (targetIndices.Count != 1 || currentIndices.Count != 1)
        {
            return null;
        }

        // 候选项按 Y 升序排列；目标在下方时发送负滚轮值。
        return currentIndices[0] - targetIndices[0];
    }

    internal static string NormalizeText(string text)
    {
        return string.Concat(text.Where(c => !char.IsWhiteSpace(c)))
            .Replace('【', '「').Replace('[', '「').Replace('】', '」').Replace(']', '」');
    }
}
