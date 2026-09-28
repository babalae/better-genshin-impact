using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

public static class TrainingGuideDiagnostics
{
    public const string DirectoryPath = @"User\Diagnostics\AutoDomain.TrainingGuide";

    public static void Save(Mat image, ILogger logger, string stage)
    {
        try
        {
            var folder = Global.Absolute(DirectoryPath);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{stage}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.png");
            if (!Cv2.ImWrite(path, image)) logger.LogWarning("培养诊断截图保存失败：{Path}", path);
            else logger.LogDebug("培养诊断截图：{Path}", path);
        }
        catch (Exception e) { logger.LogWarning("培养诊断截图保存失败：{Message}", e.Message); }
    }
}
