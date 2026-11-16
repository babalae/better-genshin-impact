using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.Ncnn;
using BetterGenshinImpact.Core.Recognition.OpenVino;
using BetterGenshinImpact.Core.Recognition.OCR.Paddle;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
///     推理后端路由：把设置页的「全局后端意图 + 各后端逐用途开关」解析成 OCR / YOLO 的实际后端。
///     解析规则（确定性、可解释）：
///     1. 全局意图后端优先：它对应用途开启且可用时直接采用；
///     2. 其余后端按「OpenVINO → ncnn」次序补位：任何后端对应用途开启且可用时优先于 ONNX；
///     3. 没有任何非 ONNX 后端可用（或全局配置尚未初始化，如单元测试）时回落 ONNX Runtime。
///     「可用」的含义：ncnn 需登记模型 + 同名产物（OCR 固定 PP-OCRv6 动态模型）；OpenVINO 需运行库已安装。
///     后端创建失败按各后端的约定抛出（NcnnUnavailableException / OpenVinoUnavailableException），
///     不在本层静默回退——启动校验已经先拦下不完整的运行库。
/// </summary>
public static class InferenceBackendRouter
{
    /// <summary>OCR 的实际生效后端。</summary>
    public enum OcrBackend
    {
        Onnx,
        OpenVino,
        Ncnn
    }

    /// <summary>YOLO 的实际生效后端。</summary>
    public enum YoloBackend
    {
        Onnx,
        OpenVino,
        Ncnn
    }

    /// <summary>
    ///     读取当前硬件加速配置（全局配置的单一读取入口，ncnn / OpenVINO 的 Startup 也经由这里）；
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
        if (config == null) return OcrBackend.Onnx;
        foreach (var candidate in OrderedBackends(config.PreferredBackend))
        {
            switch (candidate)
            {
                case InferenceBackend.OpenVino
                    when config.OpenVino.UseOcr && OpenVinoRuntime.IsInstalled():
                    return OcrBackend.OpenVino;
                case InferenceBackend.Ncnn
                    when config.Ncnn.UseOcr && HasNcnnOcrArtifacts(modelType):
                    return OcrBackend.Ncnn;
            }
        }

        return OcrBackend.Onnx;
    }

    /// <summary>解析 YOLO 的实际生效后端（不创建任何会话）。</summary>
    public static YoloBackend ResolveYolo(BgiOnnxModel model)
    {
        var config = TryGetConfig();
        if (config == null) return YoloBackend.Onnx;
        foreach (var candidate in OrderedBackends(config.PreferredBackend))
        {
            switch (candidate)
            {
                case InferenceBackend.OpenVino
                    when config.OpenVino.UseYolo && OpenVinoRuntime.IsInstalled() && OpenVinoYoloBackend.IsRegistered(model):
                    return YoloBackend.OpenVino;
                case InferenceBackend.Ncnn
                    when config.Ncnn.UseYolo && NcnnYoloBackend.IsRegistered(model):
                    return YoloBackend.Ncnn;
            }
        }

        return YoloBackend.Onnx;
    }

    /// <summary>
    ///     装配 OCR 后端组合（Det + Rec + 后端名）。ONNX 时返回 null，调用方走模型自身的 Build。
    ///     后端创建失败按约定抛出，不由本层静默回退。
    /// </summary>
    internal static (IOcrDetector Det, IOcrRecognizer Rec, string BackendName)? CreateOcrBackends(
        PaddleOcrService.PaddleOcrModelType modelType, ILogger logger)
    {
        var config = TryGetConfig();
        if (config == null) return null;
        switch (ResolveOcr(modelType))
        {
            case OcrBackend.OpenVino:
                return WithName(OpenVinoOcrBackend.Create(logger, modelType, OpenVinoBackendSettings.FromConfig(config.OpenVino)),
                    "OpenVINO");
            case OcrBackend.Ncnn:
                return WithName(NcnnOcrBackend.Create(logger, modelType, NcnnBackendSettings.FromConfig(config.Ncnn)), "ncnn");
            default:
                return null;
        }
    }

    private static (IOcrDetector Det, IOcrRecognizer Rec, string BackendName) WithName(
        (IOcrDetector Det, IOcrRecognizer Rec) backends, string backendName)
        => (backends.Det, backends.Rec, backendName);

    /// <summary>
    ///     装配 YOLO 后端；ONNX（或模型未登记）时返回 null，调用方走 YoloSharp。
    ///     后端创建失败按约定抛出，不由本层静默回退。
    /// </summary>
    internal static IYoloPredictorBackend? CreateYoloBackend(BgiOnnxModel model, ILogger logger)
    {
        var config = TryGetConfig();
        if (config == null) return null;
        switch (ResolveYolo(model))
        {
            case YoloBackend.OpenVino:
                return OpenVinoYoloBackend.Create(model, logger, OpenVinoBackendSettings.FromConfig(config.OpenVino));
            case YoloBackend.Ncnn:
                return NcnnYoloBackend.Create(model, logger, NcnnBackendSettings.FromConfig(config.Ncnn));
            default:
                return null;
        }
    }

    /// <summary>候选次序：全局意图优先，其余按 OpenVINO → ncnn 补位。</summary>
    private static InferenceBackend[] OrderedBackends(InferenceBackend preferred)
    {
        InferenceBackend[] others = preferred switch
        {
            InferenceBackend.OpenVino => [InferenceBackend.Ncnn],
            InferenceBackend.Ncnn => [InferenceBackend.OpenVino],
            _ => [InferenceBackend.OpenVino, InferenceBackend.Ncnn],
        };
        return [preferred, .. others];
    }

    /// <summary>ncnn OCR 只支持内置 PP-OCRv6（det_dyn/rec_dyn 为 V6 专属产物）；插件模型回落 ONNX。</summary>
    private static bool HasNcnnOcrArtifacts(PaddleOcrService.PaddleOcrModelType modelType)
        => ReferenceEquals(modelType.DetectionModel, BgiOnnxModel.PaddleOcrDetV6)
           && ReferenceEquals(modelType.RecognitionModel, BgiOnnxModel.PaddleOcrRecV6);
}
