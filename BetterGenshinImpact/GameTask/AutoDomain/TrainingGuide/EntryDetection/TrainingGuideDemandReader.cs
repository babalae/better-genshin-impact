using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>选中入口确认后采集两帧；OCR 串行，第一帧识别与第二帧采集重叠。</summary>
internal static class TrainingGuideDemandReader
{
    public static async Task<bool> ReadAsync(string entry, ILogger logger, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var id = $"demand-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        var started = Stopwatch.GetTimestamp();
        // 克隆区域独立持有像素，后台识别不访问截图对象或游戏输入。
        using var first = CaptureDemand();
        var firstAt = DateTime.Now;
        var ocr = OcrFactory.Paddle;
        var firstTask = Task.Run(() => ReadFrame(ocr, first));
        try
        {
            await Delay(200, ct);
            ct.ThrowIfCancellationRequested();
            using var second = CaptureDemand();
            var secondAt = DateTime.Now;
            var secondAfterMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var firstResult = await firstTask;
            ct.ThrowIfCancellationRequested();
            // 不对共享 OCR 实例发起两个并行调用。
            var secondResult = await Task.Run(() => ReadFrame(ocr, second));
            ct.ThrowIfCancellationRequested();
            var found = HasDemand(firstResult.Text) || HasDemand(secondResult.Text);
            TrainingGuideDiagnostics.LogEntryVerification(id,
                $"需求双帧；入口={entry}；A采集={firstAt:O}；B采集={secondAt:O}；采集间隔={(secondAt - firstAt).TotalMilliseconds:F0}ms；B采集完成={secondAfterMs:F0}ms；A识别耗时={firstResult.Milliseconds:F0}ms；B识别耗时={secondResult.Milliseconds:F0}ms；总耗时={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}ms；A原文=[{firstResult.Text}]；B原文=[{secondResult.Text}]；需求存在={found}");
            if (TrainingGuideDiagnostics.Enabled)
            {
                TrainingGuideDiagnostics.Save(first, "frame-a", id);
                TrainingGuideDiagnostics.Save(second, "frame-b", id);
            }
            return found;
        }
        finally
        {
            // 取消或第二帧截图失败时，也必须等后台读取结束才能释放 first。
            // 原异常由 try 中的操作传播；这里仅观察任务异常并保证资源生命周期。
            try { await firstTask; }
            catch (Exception e) { logger.LogWarning("需求首帧 OCR 失败 [{Id}]：{Message}", id, e.Message); }
        }
    }

    private static Mat CaptureDemand()
    {
        using var screen = CaptureToRectArea();
        using var region = new Mat(screen.SrcMat, new Rect((int)(screen.Width * .48),
            (int)(screen.Height * .49), (int)(screen.Width * .5), (int)(screen.Height * .33)));
        return region.Clone();
    }

    private static (string Text, double Milliseconds) ReadFrame(IOcrService ocr, Mat frame)
    {
        var started = Stopwatch.GetTimestamp();
        var text = ocr.Ocr(frame);
        return (text, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private static bool HasDemand(string text) => TrainingGuideMaterialCatalog.Normalize(text).Contains("需求角色");
}
