using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     ncnn OCR 后端的装配入口：从资产目录加载 shim、动态形状模型（det_dyn / rec_dyn）与同源字典。
///     开关开启时 Det/Rec 全部走 ncnn；产物缺失或创建失败直接抛 <see cref="NcnnUnavailableException" />，
///     由启动流程统一提示，不再静默换用 ONNX。
/// </summary>
internal static class NcnnOcrBackend
{
    /// <summary>ncnn 资产目录（相对启动目录）。</summary>
    private const string RelativeModelDir = @"Assets\Model\PaddleOCR\Ncnn";

    public static (IOcrDetector Det, IOcrRecognizer Rec) Create(ILogger logger, NcnnBackendSettings settings)
    {
        var modelDir = Global.Absolute(RelativeModelDir);
        RequireShim();

        NcnnDet? det = null;
        NcnnRec? rec = null;
        try
        {
            // ncnn 产物固定为 PpOcrV6：字典/归一化参数必须取 V6 自身的配置，
            // 不能跟随界面的 OCR 模型选择（V4/V5 字典与 V6 模型类数不同会整体错位）
            var v6 = PaddleOcrService.PaddleOcrModelType.V6;
            var labels = v6.RecLabel();
            var gpuIndex = settings.EffectiveGpuIndex;
            // det / rec 共享同一个 shim 实例（库句柄进程内缓存，导出绑定只做一次）
            INcnnShim shim = new NcnnShim();
            det = new NcnnDet(shim, modelDir, v6.DetectionVersion, settings.Det, gpuIndex, logger);
            rec = new NcnnRec(shim, modelDir, labels, settings.Rec, gpuIndex, logger);
            logger.LogInformation(
                "[OCR] ncnn 后端已启用（{Det} / {Rec}，固定 PpOcrV6，忽略界面的 OCR 模型选择），{Device}，检测 {DetThreads} 线程 / 识别 {RecThreads} 线程，输入模式：动态形状（det_dyn / rec_dyn），模型目录 {Dir}",
                det.GetConfigName, rec.GetConfigName,
                gpuIndex >= 0 ? $"Vulkan 设备 #{gpuIndex}" : "CPU 推理", settings.Det.Threads, settings.Rec.Threads,
                modelDir);
            return (det, rec);
        }
        catch (Exception e)
        {
            det?.Dispose();
            rec?.Dispose();
            logger.LogError(e, "[OCR] ncnn 后端创建失败");
            throw new NcnnUnavailableException(
                $"[OCR] ncnn 后端创建失败（模型目录 {modelDir}）：{e.Message}");
        }
    }

    /// <summary>运行库缺失或版本过旧都要在创建会话前拦下（裸名加载，缺失时在此抛可读错误）。</summary>
    private static void RequireShim()
    {
        if (!NcnnShim.IsSupported())
            throw new NcnnUnavailableException(
                NcnnShim.ShimMissingOrOutdatedText
                + "。运行库由 NuGet 包 BetterGI.NcnnShim 随构建部署，请更新包或暂时关闭 ncnn 开关。");
    }
}
