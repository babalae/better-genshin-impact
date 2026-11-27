using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>浮窗局部 OCR。每种预处理方式连续读两帧，关键数字一致才接受。</summary>
/// <param name="runtime">本次运行使用的运行环境，截图与等待都只使用它。</param>
/// <param name="logger">任务日志。</param>
/// <param name="ct">取消令牌。</param>
public sealed class TrainingGuidePopupRecognizer(GameRuntime runtime, ILogger logger, CancellationToken ct)
{
    public async Task<TrainingGuideMaterialReading?> ReadStable(TrainingGuideMaterial expectedMaterial,
        TrainingGuideEntry? entry = null)
    {
        ArgumentNullException.ThrowIfNull(expectedMaterial);
        TrainingGuideMaterialReading? previous = null;
        Rect? previousRect = null;
        var diagnostic = string.Empty;
        var parsedCount = 0;
        var hadIssue = false;
        var captureId = $"popup-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        var debugId = TrainingGuideDiagnostics.Enabled ? captureId : null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var capture = runtime.CaptureToRectArea();
            var mode = attempt / 2;
            var debugPrefix = debugId == null ? null : $"{debugId}-attempt-{attempt + 1}-mode-{mode}";
            SaveDebugImage(capture.SrcMat, debugPrefix, "capture");
            // 常用区域优先；失败后从屏底向上补扫，重叠窗口避免截断数量标签。
            TrainingGuideMaterialReading? current = null;
            var footerRect = default(Rect);
            diagnostic = $"图标材料：{expectedMaterial.Name}；所有数量区域均未解析成功";
            var regionIndex = 0;
            foreach (var region in GetQuantityRegions(capture.Width, capture.Height))
            {
                ct.ThrowIfCancellationRequested();
                var footer = ReadLines(capture, region, mode, debugPrefix, $"footer-{regionIndex++}");
                current = TrainingGuidePopupParser.ParseQuantities(expectedMaterial, string.Join("\n", footer));
                if (current == null) continue;
                footerRect = region;
                diagnostic = $"图标材料：{expectedMaterial.Name}；数量：{string.Join(" | ", footer)}";
                break;
            }
            // 不跨搜索区域拼接两帧结果，也不跨预处理方式复用上一帧。
            var consistent = current != null && previousRect == footerRect && SameNumbers(current, previous);
            var issue = current == null || (previous != null && !consistent);
            hadIssue |= issue;
            TrainingGuideDiagnostics.LogEntryVerification(captureId,
                $"尝试={attempt + 1}/6；预处理={mode}；秘境={entry?.Domain}；入口={entry?.Entry}；裁剪={footerRect}；{diagnostic}；库存={current?.Stock}；目标={current?.Required}；有需求={current?.IsTarget}；两帧一致={consistent}；异常={issue}");
            if (current != null) parsedCount++;
            if (current != null && consistent)
            {
                if (hadIssue)
                    TrainingGuideDiagnostics.AppendOcrIssue(captureId,
                        $"最终结果=重试成功；尝试次数={attempt + 1}；材料={expectedMaterial.Name}；库存={current.Stock}；目标={(current.IsTarget ? current.Required.ToString() : "-")}");
                return current;
            }
            // 更换预处理方式后重新取得两帧一致的结果。
            previous = attempt % 2 == 0 ? current : null;
            previousRect = previous == null ? null : footerRect;
            await runtime.Delay(300, ct);
        }
        TrainingGuideDiagnostics.AppendOcrIssue(captureId,
            $"最终结果=重试耗尽；材料={expectedMaterial.Name}；有效解析次数={parsedCount}/6；未取得同一预处理方式下连续两帧一致结果");
        logger.LogWarning("培养浮窗OCR失败：6次尝试中 {ParsedCount} 次字段解析成功，未取得连续两次有效且一致的结果；最后一次：{Detail}", parsedCount, diagnostic);
        return null;
    }

    /// <summary>先检查常用数量栏，再从屏高 99% 向上逐级搜索到屏幕中心。</summary>
    internal static IEnumerable<Rect> GetQuantityRegions(int width, int height)
    {
        // 保留原范围，正常弹窗无需增加 OCR 次数。
        var left = (int)(width * .365);
        var regionWidth = (int)(width * .27);
        var usualTop = (int)Math.Floor(height * (830d / 1080));
        yield return new Rect(left, usualTop, regionWidth,
            (int)Math.Ceiling(height * (1028d / 1080)) - usualTop);
        // 每段高 18%，向上移动 8%，重叠 10% 以容纳整行文字。
        for (var step = 0; step < 5; step++)
        {
            var bottom = .99 - step * .08;
            var top = (int)Math.Floor(height * Math.Max(.5, bottom - .18));
            yield return new Rect(left, top, regionWidth, (int)Math.Ceiling(height * bottom) - top);
        }
    }

    private static bool SameNumbers(TrainingGuideMaterialReading current, TrainingGuideMaterialReading? previous) =>
        previous != null && current.Material == previous.Material && current.Stock == previous.Stock &&
        current.Required == previous.Required && current.IsTarget == previous.IsTarget;

    private void SaveDebugImage(Mat image, string? prefix, string part)
    {
        if (prefix == null) return;
        TrainingGuideDiagnostics.Save(image, part, prefix);
    }

    private IReadOnlyList<string> ReadLines(ImageRegion capture, Rect bounds, int mode, string? debugPrefix, string part)
    {
        using var crop = capture.DeriveCrop(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        using var prepared = new Mat();
        if (mode == 0) crop.SrcMat.CopyTo(prepared);
        else
        {
            using var enlarged = new Mat();
            Cv2.Resize(crop.SrcMat, enlarged, new Size(crop.Width * 2, crop.Height * 2), 0, 0, InterpolationFlags.Cubic);
            if (mode == 1) enlarged.CopyTo(prepared);
            else
            {
                using var gray = new Mat();
                Cv2.CvtColor(enlarged, gray, enlarged.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
                using var binary = new Mat();
                Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                Cv2.CvtColor(binary, prepared, ColorConversionCodes.GRAY2BGR);
            }
        }
        SaveDebugImage(prepared, debugPrefix, part);
        using var region = new ImageRegion(prepared.Clone(), 0, 0);
        var blocks = region.FindMulti(RecognitionObject.Ocr(0, 0, region.Width, region.Height));
        try
        {
            // 标签和红色库存可能拆成多个 OCR 框，先按同一行合并，再解析数字。
            var lines = new List<(double Y, List<Region> Blocks)>();
            var tolerance = capture.Height * .012 * (mode == 0 ? 1 : 2);
            foreach (var block in blocks.OrderBy(b => b.Y + b.Height / 2d))
            {
                var center = block.Y + block.Height / 2d;
                if (lines.Count == 0 || Math.Abs(lines[^1].Y - center) > tolerance)
                    lines.Add((center, new List<Region>()));
                lines[^1].Blocks.Add(block);
            }
            return lines.Select(line => string.Concat(line.Blocks.OrderBy(b => b.X).Select(b => b.Text))).ToArray();
        }
        finally { foreach (var block in blocks) block.Dispose(); }
    }
}
