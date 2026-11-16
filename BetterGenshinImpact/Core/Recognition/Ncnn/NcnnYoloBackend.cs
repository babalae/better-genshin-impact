using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Compunet.YoloSharp.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>
///     YOLO 检测模型的 ncnn 后端装配入口（Vulkan 显卡或 CPU 推理）。
///     模型产物约定：与 ONNX 同目录的 Ncnn 子目录下放同名 .param/.bin（pnnx 固定形状转换），
///     例如 Assets\Model\World\Ncnn\bgi_world.param/bin。
///     开关开启后产物缺失或创建失败直接抛 <see cref="NcnnUnavailableException" />，不再静默回退 YoloSharp。
/// </summary>
internal static class NcnnYoloBackend
{
    /// <summary>
    ///     登记支持 ncnn 的内置模型。开启「YOLO 使用 ncnn」后这些模型缺产物属于错误配置，必须显式报错；
    ///     其它模型（脚本自定义 onnx）从未承诺有 ncnn 产物，缺产物时走 ONNX Runtime 才是正常路径。
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

    /// <summary>该模型是否属于登记支持 ncnn 的内置模型；未登记模型（脚本自定义）始终走 ONNX。</summary>
    public static bool IsRegistered(BgiOnnxModel model) => RegisteredModels.Contains(model);

    /// <summary>已报告过 ncnn 启用的模型：预测器每场战斗重建，启用日志每个模型只报一次。</summary>
    private static readonly HashSet<string> LoggedActivations = new();

    public static IYoloPredictorBackend Create(BgiOnnxModel model, ILogger logger, NcnnBackendSettings settings)
    {
        var gpuIndex = settings.EffectiveGpuIndex;
        var numThreads = settings.Yolo.Threads;
        var stem = Path.GetFileNameWithoutExtension(model.ModalPath);
        var ncnnDir = Path.Combine(Path.GetDirectoryName(model.ModalPath) ?? ".", "Ncnn");
        var paramPath = Path.Combine(ncnnDir, stem + ".param");
        var modelPath = Path.Combine(ncnnDir, stem + ".bin");
        if (!File.Exists(paramPath) || !File.Exists(modelPath))
            throw new NcnnUnavailableException(
                $"[YOLO] {model.Name} 没有 ncnn 产物：{paramPath} / {modelPath}；"
                + "请在设置页关闭「YOLO 使用 ncnn」或补齐 pnnx 转换产物。");

        if (!NcnnShim.IsSupported())
            throw new NcnnUnavailableException("[YOLO] " + NcnnShim.ShimMissingOrOutdatedText + "。");

        try
        {
            var (names, width, height, isClassifier) = YoloOnnxMetadata.GetOrRead(model.ModalPath);
            IYoloPredictorBackend predictor =
                new NcnnYoloPredictor(new NcnnShim(), paramPath, modelPath, names, width, height,
                    tuning: settings.Yolo, gpuIndex: gpuIndex, isClassifier: isClassifier);
            var deviceText = gpuIndex >= 0 ? $"Vulkan 设备 #{gpuIndex}" : $"CPU（{numThreads} 线程）";
            lock (LoggedActivations)
            {
                if (LoggedActivations.Add(model.Name))
                    logger.LogInformation("[YOLO] {Model} 使用 ncnn {Device} 后端（{Config}），模型目录 {Dir}",
                        model.Name, deviceText, predictor.ConfigName, ncnnDir);
            }

            return predictor;
        }
        catch (Exception e)
        {
            logger.LogError(e, "[YOLO] ncnn 后端创建失败，{Model}", model.Name);
            throw new NcnnUnavailableException($"[YOLO] {model.Name} 的 ncnn 后端创建失败：{e.Message}");
        }
    }
}
