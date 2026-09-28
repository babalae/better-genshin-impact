using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>浮窗局部 OCR。每种预处理方式连续读两帧，关键数字一致才接受。</summary>
public sealed class TrainingGuidePopupRecognizer(ILogger logger, CancellationToken ct, bool debugEnabled = false)
{
    public async Task<TrainingGuideMaterialReading?> ReadStable(TrainingGuideMaterial expectedMaterial,
        TrainingGuideEntry? entry = null)
    {
        ArgumentNullException.ThrowIfNull(expectedMaterial);
        TrainingGuideMaterialReading? previous = null;
        var diagnostic = string.Empty;
        var parsedCount = 0;
        var hadIssue = false;
        var captureId = $"popup-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        var debugId = debugEnabled ? captureId : null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var capture = CaptureToRectArea();
            var mode = attempt / 2;
            var debugPrefix = debugId == null ? null : $"{debugId}-attempt-{attempt + 1}-mode-{mode}";
            SaveDebugImage(capture.SrcMat, debugPrefix, "capture");
            // 底部从46%覆盖至98%，给较高浮窗的库存/培养需求条保留空间。
            var footerTop = (int)(capture.Height * .46);
            var footerRect = new Rect((int)(capture.Width * .365), footerTop, (int)(capture.Width * .27),
                (int)(capture.Height * .98) - footerTop);
            if (debugEnabled) logger.LogInformation("培养浮窗数量裁剪：{Footer}", footerRect);
            var footer = ReadLines(capture, footerRect, mode, debugPrefix, "footer");
            var current = TrainingGuidePopupParser.ParseQuantities(expectedMaterial, string.Join("\n", footer));
            diagnostic = $"图标材料：{expectedMaterial.Name}；底部：{string.Join(" | ", footer)}";
            var consistent = current != null && SameNumbers(current, previous);
            // 第一帧尚无对照，不算异常；解析失败或已有两帧结果不一致才落盘。
            if (current == null || (previous != null && !consistent))
            {
                hadIssue = true;
                TrainingGuideDiagnostics.AppendOcrIssue(logger, captureId,
                    $"尝试 {attempt + 1}/6，预处理 {mode}；秘境={entry?.Domain}；入口={entry?.Entry}；预期材料={expectedMaterial.Name}；" +
                    $"{diagnostic}；结果={current?.Material.Name ?? "解析失败"}；身份来源=图标；" +
                    $"库存={current?.Stock}；目标={(current?.IsTarget == true ? current.Required.ToString() : "-")}；两帧一致={consistent}");
            }
            if (current != null) parsedCount++;
            if (debugEnabled)
                logger.LogInformation("培养浮窗OCR尝试 {Attempt}/6，预处理 {Mode}，字段解析 {Parsed}，与前帧一致 {Consistent}：{Detail}",
                    attempt + 1, mode, current != null, current != null && SameNumbers(current, previous), diagnostic);
            if (current != null && consistent)
            {
                if (hadIssue)
                    TrainingGuideDiagnostics.AppendOcrIssue(logger, captureId,
                        $"最终结果=重试成功；尝试次数={attempt + 1}；材料={expectedMaterial.Name}；库存={current.Stock}；目标={(current.IsTarget ? current.Required.ToString() : "-")}");
                return current;
            }
            // 更换预处理方式后重新取得两帧一致的结果。
            previous = attempt % 2 == 0 ? current : null;
            await Delay(300, ct);
        }
        TrainingGuideDiagnostics.AppendOcrIssue(logger, captureId,
            $"最终结果=重试耗尽；材料={expectedMaterial.Name}；有效解析次数={parsedCount}/6；未取得同一预处理方式下连续两帧一致结果");
        logger.LogWarning("培养浮窗OCR失败：6次尝试中 {ParsedCount} 次字段解析成功，未取得连续两次有效且一致的结果；最后一次：{Detail}", parsedCount, diagnostic);
        return null;
    }

    public static bool SameNumbers(TrainingGuideMaterialReading current, TrainingGuideMaterialReading? previous) =>
        previous != null && current.Material == previous.Material && current.Stock == previous.Stock &&
        current.Required == previous.Required && current.IsTarget == previous.IsTarget;

    private void SaveDebugImage(Mat image, string? prefix, string part)
    {
        if (prefix == null) return;
        TrainingGuideDiagnostics.Save(image, logger, part, prefix);
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
