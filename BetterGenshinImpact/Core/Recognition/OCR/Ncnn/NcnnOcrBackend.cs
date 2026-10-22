using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.OCR.Ncnn;

/// <summary>
///     ncnn Vulkan 后端的装配入口：从资产目录加载 shim、分桶模型与同源字典。
///     开关开启时 Det/Rec 全部走 ncnn；前置产物缺失或加载失败返回 null，由调用方回退所选推理引擎。
/// </summary>
internal static class NcnnOcrBackend
{
    /// <summary>ncnn 资产目录（相对启动目录）。</summary>
    private const string RelativeModelDir = @"Assets\Model\PaddleOCR\Ncnn";

    /// <summary>
    ///     尝试创建 ncnn 后端组合。失败时返回 null 并通过 <paramref name="logger" /> 记录原因（调用方回退）。
    /// </summary>
    public static (IOcrDetector Det, IOcrRecognizer Rec)? TryCreate(ILogger logger)
    {
        var modelDir = Global.Absolute(RelativeModelDir);
        var shimPath = Path.Combine(modelDir, "ncnn_shim.dll");
        if (!File.Exists(shimPath))
        {
            logger.LogWarning("[OCR] ncnn 开关已开启但 shim 缺失，回退所选推理引擎: {Path}", shimPath);
            return null;
        }

        NcnnDet? det = null;
        NcnnRec? rec = null;
        try
        {
            // ncnn 产物固定为 PpOcrV6：字典/归一化参数必须取 V6 自身的配置，
            // 不能跟随界面的 OCR 模型选择（V4/V5 字典与 V6 模型类数不同会整体错位）
            var v6 = PaddleOcrService.PaddleOcrModelType.V6;
            var labels = v6.RecLabel();
            det = new NcnnDet(shimPath, modelDir, v6.DetectionVersion);
            rec = new NcnnRec(shimPath, modelDir, labels);
            logger.LogInformation("[OCR] ncnn Vulkan 后端已启用（{Det} / {Rec}，固定 PpOcrV6，忽略界面的 OCR 模型选择），模型目录 {Dir}",
                det.GetConfigName, rec.GetConfigName, modelDir);
            return (det, rec);
        }
        catch (Exception e)
        {
            det?.Dispose();
            rec?.Dispose();
            logger.LogWarning(e, "[OCR] ncnn 后端创建失败，回退所选推理引擎");
            return null;
        }
    }
}
