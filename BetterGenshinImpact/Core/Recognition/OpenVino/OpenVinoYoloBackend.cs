using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Core.Recognition.OpenVino;

/// <summary>
///     YOLO 检测模型的 OpenVINO 后端装配入口（直接推理现有 ONNX 模型文件，无需转换产物）。
///     开关开启后运行库缺失或创建失败直接抛 <see cref="OpenVinoUnavailableException" />，不静默回退 YoloSharp。
/// </summary>
internal static class OpenVinoYoloBackend
{
    /// <summary>
    ///     登记支持 OpenVINO 的内置模型（与 ncnn 产物登记一致：这些模型常驻推理路径，切换后必须真实可用）。
    ///     OpenVINO 直接推理 .onnx，脚本自定义模型理论上也可用，但保持与 ncnn 相同的收紧路由，
    ///     避免「开关开着却撞上未经验证的模型」。
    /// </summary>
    private static readonly HashSet<BgiOnnxModel> RegisteredModels =
    [
        BgiOnnxModel.BgiWorld,
        BgiOnnxModel.BgiFish,
        BgiOnnxModel.BgiMine,
        BgiOnnxModel.BgiTree,
        BgiOnnxModel.BgiQClassify,
        BgiOnnxModel.BgiAvatarSide,
        BgiOnnxModel.BgiEClassify
    ];

    /// <summary>该模型是否属于登记支持 OpenVINO 的内置模型；未登记模型（脚本自定义）始终走 ONNX。</summary>
    public static bool IsRegistered(BgiOnnxModel model) => RegisteredModels.Contains(model);

    /// <summary>已报告过 OpenVINO 启用的模型：预测器每场战斗重建，启用日志每个模型只报一次。</summary>
    private static readonly HashSet<string> LoggedActivations = new();

    public static IYoloPredictorBackend Create(BgiOnnxModel model, ILogger logger, OpenVinoBackendSettings settings)
    {
        var missing = OpenVinoRuntime.FirstMissingFile();
        if (missing != null)
            throw new OpenVinoUnavailableException(
                $"[YOLO] OpenVINO 运行库缺失（{missing}）。请在硬件加速设置中下载运行库，或关闭「YOLO 使用 OpenVINO」。");

        try
        {
            var (names, width, height, isClassifier) = YoloOnnxMetadata.GetOrRead(model.ModalPath);
            IYoloPredictorBackend predictor =
                new OpenVinoYoloPredictor(model.ModalPath, settings.DeviceId, settings.EnableModelCache,
                    names, width, height, isClassifier: isClassifier);
            lock (LoggedActivations)
            {
                if (LoggedActivations.Add(model.Name))
                    logger.LogInformation("[YOLO] {Model} 使用 OpenVINO {Device} 后端（{Config}）",
                        model.Name, settings.DeviceId, predictor.ConfigName);
            }

            return predictor;
        }
        catch (Exception e)
        {
            logger.LogError(e, "[YOLO] OpenVINO 后端创建失败，{Model}", model.Name);
            throw new OpenVinoUnavailableException($"[YOLO] {model.Name} 的 OpenVINO 后端创建失败：{e.Message}", e);
        }
    }
}
