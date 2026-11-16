using System;
using System.Collections.Concurrent;
using Compunet.YoloSharp.Metadata;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
///     YOLO ONNX 模型元数据读取（类别名、静态输入尺寸、任务类型），ncnn 与 OpenVINO 后端共用。
///     用一次性 CPU 会话读取一次，进程内按模型路径缓存。
/// </summary>
internal static class YoloOnnxMetadata
{
    private static readonly ConcurrentDictionary<string, (YoloName[] Names, int Width, int Height, bool IsClassifier)> Cache = new();

    public static (YoloName[] Names, int Width, int Height, bool IsClassifier) GetOrRead(string onnxPath)
        => Cache.GetOrAdd(onnxPath, Read);

    /// <summary>用一次性 CPU 会话读 Ultralytics 元数据（names/task）与静态输入尺寸；仅检测/分类可用。</summary>
    private static (YoloName[] Names, int Width, int Height, bool IsClassifier) Read(string onnxPath)
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
                default: throw new InvalidOperationException($"后端不支持任务类型 {task}: {onnxPath}");
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
