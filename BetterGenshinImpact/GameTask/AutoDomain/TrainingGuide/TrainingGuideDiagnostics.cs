using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

public static class TrainingGuideDiagnostics
{
    public const string DirectoryPath = @"User\Diagnostics\AutoDomain.TrainingGuide";
    private static readonly object OcrLogLock = new();

    public static void AppendOcrIssue(ILogger logger, string captureId, string message)
        => AppendText(logger, "ocr-issues.log", $"[{captureId}] {message}");

    public static void AppendScan(ILogger logger, string scanId, string message)
        => AppendText(logger, $"{scanId}.log", message);

    private static void AppendText(ILogger logger, string fileName, string message)
    {
        try
        {
            var folder = Global.Absolute(DirectoryPath);
            Directory.CreateDirectory(folder);
            lock (OcrLogLock)
                File.AppendAllText(Path.Combine(folder, fileName),
                    $"{DateTime.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception e) { logger.LogWarning("培养 OCR 记录保存失败：{Message}", e.Message); }
    }

    public static void Save(Mat image, ILogger logger, string stage, string? captureId = null)
    {
        try
        {
            var folder = Global.Absolute(DirectoryPath);
            Directory.CreateDirectory(folder);
            // 同一轮弹窗 OCR 共用标识，避免每张裁图重复追加时间戳和 GUID。
            captureId ??= $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
            var path = Path.Combine(folder, $"{captureId}-{stage}.png");
            if (!Cv2.ImWrite(path, image)) logger.LogWarning("培养诊断截图保存失败：{Path}", path);
            else logger.LogDebug("培养诊断截图：{Path}", path);
        }
        catch (Exception e) { logger.LogWarning("培养诊断截图保存失败：{Message}", e.Message); }
    }
}
