using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.Ncnn;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
///     推理后端路由：把设置页的「全局后端意图 + 各后端逐用途开关」解析成 OCR / YOLO 的实际后端。
///     解析规则（确定性、可解释）：
///     1. 逐用途开关（<c>Ncnn.UseOcr</c> / <c>Ncnn.UseYolo</c>）是唯一判据——关闭即回落 ONNX Runtime；
///     2. ncnn 还需「登记模型 + 同名产物」齐备才算可用（OCR 固定 PP-OCRv6 动态模型）；
///     3. 全局意图（<see cref="HardwareAccelerationConfig.PreferredBackend" />）只描述设置页的选择方向，
///        不改变解析结果：全局 ONNX、但用户单独开启 YOLO 的 ncnn 开关时，YOLO 仍走 ncnn。
///     OpenVINO 是 ONNX Runtime 的执行提供程序（<see cref="InferenceDeviceType.OpenVINO" />），
///     随引擎选择在会话配置层生效，不参与本路由。
///     后端创建失败按各后端的约定抛出（NcnnUnavailableException），不在本层静默回退——
///     启动校验已经先拦下不完整的运行库。
/// </summary>
public static class InferenceBackendRouter
{
    /// <summary>OCR 的实际生效后端。</summary>
    public enum OcrBackend
    {
        Onnx,
        Ncnn
    }

    /// <summary>YOLO 的实际生效后端。</summary>
    public enum YoloBackend
    {
        Onnx,
        Ncnn
    }

    /// <summary>
    ///     读取当前硬件加速配置（全局配置的单一读取入口，ncnn 的 Startup 也经由这里）；
    ///     宿主环境（如单元测试）尚未初始化全局配置时返回 null，所有路由按 ONNX 处理，保持 ONNX-only 路径可用。
    /// </summary>
    public static HardwareAccelerationConfig? TryGetConfig()
    {
        try
        {
            return Service.ConfigService.Config?.HardwareAccelerationConfig;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>解析 OCR 的实际生效后端（不创建任何会话，供诊断展示与装配共用）。</summary>
    public static OcrBackend ResolveOcr(PaddleOcrService.PaddleOcrModelType modelType)
    {
        var config = TryGetConfig();
        return config != null && config.Ncnn.UseOcr && HasNcnnOcrArtifacts(modelType)
            ? OcrBackend.Ncnn
            : OcrBackend.Onnx;
    }

    /// <summary>解析 YOLO 的实际生效后端（不创建任何会话）。</summary>
    public static YoloBackend ResolveYolo(BgiOnnxModel model)
    {
        var config = TryGetConfig();
        return config != null && config.Ncnn.UseYolo && NcnnYoloBackend.IsRegistered(model)
            ? YoloBackend.Ncnn
            : YoloBackend.Onnx;
    }

    /// <summary>
    ///     装配 OCR 后端组合（Det + Rec + 后端名）。ONNX 时返回 null，调用方走模型自身的 Build。
    ///     后端创建失败按约定抛出，不由本层静默回退。
    /// </summary>
    internal static (IOcrDetector Det, IOcrRecognizer Rec, string BackendName)? CreateOcrBackends(
        PaddleOcrService.PaddleOcrModelType modelType, ILogger logger)
    {
        var config = TryGetConfig();
        if (config == null || ResolveOcr(modelType) != OcrBackend.Ncnn) return null;
        var (det, rec) = NcnnOcrBackend.Create(logger, modelType, NcnnBackendSettings.FromConfig(config.Ncnn));
        return (det, rec, "ncnn");
    }

    /// <summary>
    ///     装配 YOLO 后端；ONNX（或模型未登记）时返回 null，调用方走 YoloSharp。
    ///     后端创建失败按约定抛出，不由本层静默回退。
    /// </summary>
    internal static IYoloPredictorBackend? CreateYoloBackend(BgiOnnxModel model, ILogger logger)
    {
        var config = TryGetConfig();
        if (config == null || ResolveYolo(model) != YoloBackend.Ncnn) return null;
        return NcnnYoloBackend.Create(model, logger, NcnnBackendSettings.FromConfig(config.Ncnn));
    }

    /// <summary>ncnn OCR 只支持内置 PP-OCRv6（det_dyn/rec_dyn 为 V6 专属产物）；插件模型回落 ONNX。</summary>
    private static bool HasNcnnOcrArtifacts(PaddleOcrService.PaddleOcrModelType modelType)
        => ReferenceEquals(modelType.DetectionModel, BgiOnnxModel.PaddleOcrDetV6)
           && ReferenceEquals(modelType.RecognitionModel, BgiOnnxModel.PaddleOcrRecV6);
}
