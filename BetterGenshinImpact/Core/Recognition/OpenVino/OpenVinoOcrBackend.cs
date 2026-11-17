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
///     静态形状策略：Rec 在 NPU 固定单宽度（<see cref="OpenVinoBackendSettings.NpuRecWidth" />）、
///     在 GPU 走宽度桶（<see cref="OpenVinoBackendSettings.GpuRecWidthBuckets" />，空串保持动态）；
///     Det 按实际运行设备决定：GPU/NPU 用固定正方形画布（<see cref="OpenVinoBackendSettings.DetStaticSide" />，
///     Intel PP-OCRv6 官方方案，等比缩放 + 右下补零 + 框坐标反映射），CPU 恒动态保召回。
///     NPU 时 Det 默认留 NPU，可配置回落（<see cref="OpenVinoBackendSettings.NpuDetDevice" />）；
///     规避动态形状的逐尺寸 kernel 编译卡顿；CPU 路线保持全动态。
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
            // NPU 编译器要求静态形状：Rec 固定为 [1,3,模型高度,配置宽度]（不足补零，CTC 解码容忍 padding）。
            // Det 按实际运行设备决定形状：GPU/NPU 用固定正方形画布（Intel PP-OCRv6 方案，
            // 等比缩放 + 右下补零 + 框坐标反映射），消除逐截图尺寸的编译卡顿；CPU 恒动态保召回。
            var isNpu = OpenVinoBackendSettings.IsNpuDevice(settings.DeviceId);
            var detDevice = isNpu ? settings.NpuDetDevice : settings.DeviceId;
            var detStaticSide = settings.DetStaticSide;
            long[]? detShape = detStaticSide > 0 && OpenVinoBackendSettings.SupportsStaticShape(detDevice)
                ? [1, 3, detStaticSide, detStaticSide]
                : null;
            var recHeight = modelType.RecognitionVersion.Shape.Height;
            var gpuBuckets = OpenVinoBackendSettings.IsGpuDevice(settings.DeviceId)
                ? OpenVinoBackendSettings.ParseGpuRecWidthBuckets(settings.GpuRecWidthBuckets)
                : [];

            det = new Det(modelType.DetectionVersion,
                new OpenVinoSession(modelType.DetectionModel.ModalPath, detDevice, settings.EnableModelCache,
                    detShape));
            // NPU 单桶与 GPU 多桶共用分桶会话（同一补零与编译缓存机制）；CPU / 显式空桶保持动态。
            IOcrInferenceSession recSession = isNpu
                ? new OpenVinoBucketedSession(modelType.RecognitionModel.ModalPath, settings.DeviceId,
                    settings.EnableModelCache, recHeight, [settings.NpuRecWidth])
                : gpuBuckets.Length > 0
                    ? new OpenVinoBucketedSession(modelType.RecognitionModel.ModalPath, settings.DeviceId,
                        settings.EnableModelCache, recHeight, gpuBuckets)
                    : new OpenVinoSession(modelType.RecognitionModel.ModalPath, settings.DeviceId, settings.EnableModelCache);
            rec = new Rec(modelType.RecLabel(), modelType.RecognitionVersion, recSession);
            logger.LogInformation("[OCR] Det 形状策略：设备 {DetDevice}，{Shape}",
                detDevice,
                detShape == null ? "动态形状（CPU 保召回）" : $"静态画布 {detStaticSide}x{detStaticSide}（等比缩放+补零+坐标反映射）");
            if (isNpu)
            {
                logger.LogInformation(
                    "[OCR] NPU 静态形状策略：Rec 固定宽度 {Width}（{Device}），Det 设备 {DetDevice}",
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
