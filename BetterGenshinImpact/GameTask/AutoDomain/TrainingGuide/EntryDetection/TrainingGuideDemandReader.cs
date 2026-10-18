using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition.OCR;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>在调用方持有的奖励截图上执行一次需求 OCR，不重新采集或修改截图。</summary>
internal static class TrainingGuideDemandReader
{
    public static async Task<bool> ReadAsync(string entry, Mat frame, Rect band, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var id = $"demand-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
        var started = Stopwatch.GetTimestamp();
        if (TrainingGuideDiagnostics.Enabled)
            TrainingGuideDiagnostics.Save(frame, "frame", id);
        var ocr = OcrFactory.Paddle;
        // 取消不能提前结束等待，确保调用方释放 Mat 前后台 OCR 已完成。
        var result = await Task.Run(() =>
        {
            var ocrStarted = Stopwatch.GetTimestamp();
            var text = ocr.Ocr(frame);
            return (Text: text, Milliseconds: Stopwatch.GetElapsedTime(ocrStarted).TotalMilliseconds);
        });
        ct.ThrowIfCancellationRequested();
        var found = TrainingGuideMaterialCatalog.Normalize(result.Text).Contains("需求角色");
        TrainingGuideDiagnostics.LogEntryVerification(id,
            $"需求单帧；入口={entry}；区域={band}；识别耗时={result.Milliseconds:F0}ms；总耗时={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0}ms；原文=[{result.Text}]；需求存在={found}");
        return found;
    }
}
