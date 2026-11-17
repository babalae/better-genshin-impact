using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.OpenVino;

/// <summary>
///     OpenVINO OCR 后端的装配入口：直接推理现有 ONNX 模型文件（slim.onnx），
///     与 ONNX Runtime 路线共用 Det / Rec 的全部预处理、后处理与解码，仅替换推理会话。
///     开关开启时 Det/Rec 全部走 OpenVINO；运行库缺失或创建失败直接抛 <see cref="OpenVinoUnavailableException" />，
///     由启动流程统一提示，不静默换用 ONNX。
///     静态形状策略：NPU 的 Rec 固定单宽度（<see cref="OpenVinoBackendSettings.NpuRecWidth" />），
///     Det 回落 <see cref="OpenVinoBackendSettings.NpuDetDevice" />；
///     GPU 的 Rec 走宽度桶（<see cref="OpenVinoBackendSettings.GpuRecWidthBuckets" />，空串保持动态），
///     规避 GPU 动态形状预览特性的逐形状 kernel 编译卡顿；CPU 保持全动态形状。
/// </summary>
internal static class OpenVinoOcrBackend
{
    public static (IOcrDetector Det, IOcrRecognizer Rec) Create(ILogger logger, PaddleOcrService.PaddleOcrModelType modelType,
        OpenVinoBackendSettings settings)
    {
        Det? det = null;
        Rec? rec = null;
        try
        {
            // NPU 编译器要求静态形状：Rec 固定为 [1,3,模型高度,配置宽度]（不足补零，CTC 解码容忍 padding）；
            // Det 输入尺寸随截图变化，NPU 无法承载，回落到配置设备（默认 CPU）。
            var isNpu = OpenVinoBackendSettings.IsNpuDevice(settings.DeviceId);
            var detDevice = isNpu ? settings.NpuDetDevice : settings.DeviceId;
            var recHeight = modelType.RecognitionVersion.Shape.Height;
            var gpuBuckets = OpenVinoBackendSettings.IsGpuDevice(settings.DeviceId)
                ? OpenVinoBackendSettings.ParseGpuRecWidthBuckets(settings.GpuRecWidthBuckets)
                : [];

            det = new Det(modelType.DetectionVersion,
                new OpenVinoSession(modelType.DetectionModel.ModalPath, detDevice, settings.EnableModelCache));
            // NPU 单桶与 GPU 多桶共用分桶会话（同一补零与编译缓存机制）；CPU / 显式空桶保持动态。
            IOcrInferenceSession recSession = isNpu
                ? new OpenVinoBucketedSession(modelType.RecognitionModel.ModalPath, settings.DeviceId,
                    settings.EnableModelCache, recHeight, [settings.NpuRecWidth])
                : gpuBuckets.Length > 0
                    ? new OpenVinoBucketedSession(modelType.RecognitionModel.ModalPath, settings.DeviceId,
                        settings.EnableModelCache, recHeight, gpuBuckets)
                    : new OpenVinoSession(modelType.RecognitionModel.ModalPath, settings.DeviceId, settings.EnableModelCache);
            rec = new Rec(modelType.RecLabel(), modelType.RecognitionVersion, recSession);
            if (isNpu)
            {
                logger.LogInformation(
                    "[OCR] NPU 静态形状策略：Rec 固定宽度 {Width}（{Device}），Det 回落 {DetDevice}",
                    settings.NpuRecWidth, settings.DeviceId, detDevice);
            }
            else if (gpuBuckets.Length > 0)
            {
                logger.LogInformation(
                    "[OCR] GPU 静态宽度桶：{Widths}（{Device}），超出最大桶的文本行压缩到最大桶",
                    string.Join("/", gpuBuckets), settings.DeviceId);
            }

            logger.LogInformation(
                "[OCR] OpenVINO 后端已启用（{Det} / {Rec}），设备 {Device}，缓存 {Cache}",
                det.GetConfigName, rec.GetConfigName, settings.DeviceId,
                settings.EnableModelCache ? "已启用" : "未启用");
            return (det, rec);
        }
        catch (Exception e)
        {
            det?.Dispose();
            rec?.Dispose();
            logger.LogError(e, "[OCR] OpenVINO 后端创建失败");
            throw new OpenVinoUnavailableException(
                $"[OCR] OpenVINO 后端创建失败（设备 {settings.DeviceId}）：{e.Message}", e);
        }
    }
}
