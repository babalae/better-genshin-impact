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
            det = new Det(modelType.DetectionVersion,
                new OpenVinoSession(modelType.DetectionModel.ModalPath, settings.DeviceId, settings.EnableModelCache));
            rec = new Rec(modelType.RecLabel(), modelType.RecognitionVersion,
                new OpenVinoSession(modelType.RecognitionModel.ModalPath, settings.DeviceId, settings.EnableModelCache));
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
