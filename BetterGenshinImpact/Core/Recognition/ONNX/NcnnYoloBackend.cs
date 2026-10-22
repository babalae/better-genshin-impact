using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using Compunet.YoloSharp.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
///     YOLO 检测模型的 ncnn Vulkan 后端装配入口。
///     模型产物约定：与 ONNX 同目录的 Ncnn 子目录下放同名 .param/.bin（pnnx 固定形状转换），
///     例如 Assets\Model\World\Ncnn\bgi_world.param/bin。缺失或创建失败时返回 null，调用方回退 YoloSharp。
/// </summary>
internal static class NcnnYoloBackend
{
    /// <summary>shim 复用 OCR 的 ncnn_shim.dll（通用 ncnn 封装，与检测模型无绑定）。</summary>
    private const string ShimRelativePath = @"Assets\Model\PaddleOCR\Ncnn\ncnn_shim.dll";

    /// <summary>ONNX 元数据（类别名、输入尺寸、任务类型）按模型路径缓存：CPU 临时会话读取一次，进程内复用。</summary>
    private static readonly ConcurrentDictionary<string, (YoloName[] Names, int Width, int Height, bool IsClassifier)> MetadataCache = new();

    /// <summary>已向日志栏报告过 ncnn 启用的模型：预测器每场战斗重建，启用日志每个模型只报一次。</summary>
    private static readonly HashSet<string> LoggedActivations = new();

    public static NcnnYoloPredictor? TryCreate(BgiOnnxModel model, ILogger logger)
    {
        try
        {
            var stem = Path.GetFileNameWithoutExtension(model.ModalPath);
            var ncnnDir = Path.Combine(Path.GetDirectoryName(model.ModalPath) ?? ".", "Ncnn");
            var paramPath = Path.Combine(ncnnDir, stem + ".param");
            var modelPath = Path.Combine(ncnnDir, stem + ".bin");
            if (!File.Exists(paramPath) || !File.Exists(modelPath))
            {
                // 尚未转换 ncnn 产物的模型（BgiFish/BgiMine/BgiTree…）静默回退，避免每个预测器创建都刷警告
                logger.LogDebug("[YOLO] {Model} 无 ncnn 产物，使用所选推理引擎: {Param}", model.Name, paramPath);
                return null;
            }

            var shimPath = Global.Absolute(ShimRelativePath);
            if (!File.Exists(shimPath))
            {
                logger.LogWarning("[YOLO] ncnn 开关已开启但 shim 缺失，{Model} 回退所选推理引擎: {Path}", model.Name, shimPath);
                return null;
            }

            var (names, width, height, isClassifier) = MetadataCache.GetOrAdd(model.ModalPath, ReadMetadata);
            var predictor = new NcnnYoloPredictor(shimPath, paramPath, modelPath, names, width, height,
                isClassifier: isClassifier);
            string activationLine;
            lock (LoggedActivations)
            {
                // 后续重建只落文件日志（Debug），日志栏不重复刷
                activationLine = LoggedActivations.Add(model.Name)
                    ? $"[YOLO] {model.Name} 使用 ncnn Vulkan 后端（{predictor.ConfigName}），模型目录 {ncnnDir}"
                    : "";
            }

            if (activationLine.Length > 0) logger.LogInformation("{Activation}", activationLine);
            else logger.LogDebug("[YOLO] {Model} ncnn Vulkan 后端已重建（{Config}）", model.Name, predictor.ConfigName);
            return predictor;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "[YOLO] ncnn 后端创建失败，{Model} 回退所选推理引擎", model.Name);
            return null;
        }
    }

    /// <summary>用一次性 CPU 会话读 Ultralytics 元数据（names/task）与静态输入尺寸；仅检测/分类可用。</summary>
    private static (YoloName[] Names, int Width, int Height, bool IsClassifier) ReadMetadata(string onnxPath)
    {
        using var options = new SessionOptions();
        options.AppendExecutionProvider_CPU();
        using var session = new InferenceSession(onnxPath, options);
        var metadata = session.ModelMetadata.CustomMetadataMap;
        var isClassifier = false;
        if (metadata.TryGetValue("task", out var task))
        {
            switch (task)
            {
                case "detect": break;
                case "classify": isClassifier = true; break;
                default: throw new InvalidOperationException($"ncnn 后端不支持任务类型 {task}: {onnxPath}");
            }
        }

        if (!metadata.TryGetValue("names", out var namesText))
            throw new InvalidOperationException($"模型缺少 names 元数据: {onnxPath}");
        var names = ParseNames(namesText);
        if (names.Length == 0)
            throw new InvalidOperationException($"names 元数据解析为空: {namesText}");

        foreach (var input in session.InputMetadata.Values)
        {
            var dims = input.Dimensions;
            if (dims.Length == 4 && dims[2] > 0 && dims[3] > 0)
                return (names, dims[3], dims[2], isClassifier);
        }

        throw new InvalidOperationException($"模型输入不是静态 NCHW: {onnxPath}");
    }

    /// <summary>
    ///     解析 Ultralytics names 元数据，逐字符复刻 YoloSharp YoloMetadata.ParseNames
    ///     （去大括号、", " 切分、": " 切分、去引号、下划线换空格），保证与被替换路线的类别名完全一致
    ///     （如 'health_bar' → "health bar"）。
    /// </summary>
    private static YoloName[] ParseNames(string text)
    {
        text = text[1..^1]; // '{0: 'a', 1: 'b'}' => '0: 'a', 1: 'b''
        var split = text.Split(", ");
        var names = new YoloName[split.Length];
        for (var i = 0; i < split.Length; i++)
        {
            var valueSplit = split[i].Split(": ");
            var id = int.Parse(valueSplit[0]);
            var name = valueSplit[1][1..^1].Replace('_', ' ');
            names[id] = new YoloName(id, name);
        }

        return names;
    }
}
