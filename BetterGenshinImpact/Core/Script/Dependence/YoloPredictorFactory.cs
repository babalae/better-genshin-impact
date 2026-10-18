using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Script.Utils;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using Compunet.YoloSharp;
using Compunet.YoloSharp.Data;
using Compunet.YoloSharp.Metadata;
using Microsoft.ClearScript;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BetterGenshinImpact.Core.Script.Dependence;

public sealed class YoloPredictorFactory(string rootPath)
{
    /// <summary>
    /// 创建基于 ONNX 模型的 YOLO 预测器
    /// </summary>
    /// <param name="modelPath">相对于脚本工程根目录的模型路径</param>
    /// <param name="options">可选推理配置；未指定的属性使用 <see cref="YoloConfiguration"/> 默认值</param>
    /// <remarks>
    /// <paramref name="options"/> 使用 <see cref="YoloConfiguration"/> 的属性名，作为预测器的基准配置
    /// 支持以下常用项：
    /// <list type="bullet">
    ///   <item><description><c>confidence</c>：置信度阈值（默认 0.3）</description></item>
    ///   <item><description><c>iou</c>：NMS 的 IoU 阈值（默认 0.45）</description></item>
    ///   <item><description><c>keepAspectRatio</c>：是否保持宽高比并留边（默认 true）</description></item>
    ///   <item><description><c>applyAutoOrient</c>：是否根据图像方向标记校正图像（默认 true）/></description></item>
    ///   <item><description><c>suppressParallelInference</c>：是否等待并发推理（默认 false）</description></item>
    /// </list>
    /// </remarks>
    public ScriptYoloPredictor CreatePredictor(string modelPath, ScriptObject? options = null)
    {
        modelPath = ScriptUtils.NormalizePath(rootPath, modelPath);
        if (!modelPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("模型文件必须是.onnx");
        }

        var configuration = MergeConfiguration(YoloConfiguration.Default, options);
        return new ScriptYoloPredictor(modelPath, configuration);
    }

    internal static YoloConfiguration MergeConfiguration(YoloConfiguration baseConfiguration, ScriptObject? options)
    {
        var configuration = new YoloConfiguration();
        var properties = typeof(YoloConfiguration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanWrite);

        foreach (var property in properties)
        {
            property.SetValue(configuration, property.GetValue(baseConfiguration));

            var propertyName = options?.PropertyNames.FirstOrDefault(name =>
                string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));
            if (propertyName is null) continue;

            var value = options!.GetProperty(propertyName);
            if (value is not Undefined and not null)
            {
                property.SetValue(configuration, Convert.ChangeType(value, property.PropertyType));
            }
        }

        return configuration;
    }
}

public sealed class ScriptYoloPredictor : IDisposable
{
    private readonly object _gate = new();
    private readonly YoloConfiguration _configuration;
    private readonly string _modelPath;
    private BgiYoloPredictor? _bgiPredictor;
    private bool _disposed;

    internal ScriptYoloPredictor(string modelPath, YoloConfiguration configuration)
    {
        _modelPath = modelPath;
        _configuration = configuration;
    }

    /// <summary>
    /// 对当前游戏画面做检测（可选择使用自定义检测参数）
    /// </summary>
    public List<YoloRegion> Detect(ScriptObject? options = null)
    {
        return RunDetection(options is null ? _configuration : YoloPredictorFactory.MergeConfiguration(_configuration, options));
    }

    private List<YoloRegion> RunDetection(YoloConfiguration configuration)
    {
        using var ra = TaskControl.CaptureToRectArea();
        return GetInnerPredictor(YoloTask.Detect).Detect(ra.CacheImage, configuration).Select(box =>
        {
            var bounds = box.Bounds;
            return new YoloRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height, box, ra);
        }).ToList();
    }

    /// <summary>
    /// 对当前游戏画面做分类（可选择使用自定义推理参数）
    /// </summary>
    /// <returns>按置信度从高到低排列的分类结果，第一项为最可能的类别</returns>
    /// <remarks>
    /// 分类模型会返回全部类别；<see cref="YoloConfiguration.Confidence"/> 和
    /// <see cref="YoloConfiguration.IoU"/> 不会对分类结果进行筛选。
    /// </remarks>
    public List<YoloClassification> Classify(ScriptObject? options = null)
    {
        var configuration = options is null
            ? _configuration
            : YoloPredictorFactory.MergeConfiguration(_configuration, options);

        using var ra = TaskControl.CaptureToRectArea();
        return GetInnerPredictor(YoloTask.Classify).Classify(ra.CacheImage, configuration)
            .Select(classification => new YoloClassification(classification))
            .ToList();
    }

    private YoloPredictor GetInnerPredictor(YoloTask expectedTask)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var predictor = (_bgiPredictor ??= CreateBgiPredictor()).Predictor;
            if (predictor.Metadata.Task != expectedTask)
            {
                throw new InvalidOperationException($"当前模型的任务类型为 {predictor.Metadata.Task}，不能执行 {expectedTask} 推理");
            }

            return predictor;
        }
    }

    private BgiYoloPredictor CreateBgiPredictor()
    {
        var cacheRelativePath = Path.Combine(BgiOnnxModel.ModelCacheRelativePath, "js",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_modelPath))));
        var model = new BgiOnnxModel(Path.GetFileNameWithoutExtension(_modelPath), _modelPath, cacheRelativePath);
        var predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(model);
        try
        {
            var yolo = predictor.Predictor;
            if (yolo.Metadata.Task is not (YoloTask.Detect or YoloTask.Classify) || yolo.Metadata.Names.Length == 0)
            {
                throw new InvalidOperationException("模型必须是类别名非空的检测或分类模型");
            }

            return predictor;
        }
        catch
        {
            predictor.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _bgiPredictor?.Dispose();
            _bgiPredictor = null;
        }
    }
}

public class YoloRegion(int x, int y, int width, int height, YoloPrediction detection, Region owner)
    : Region(x, y, width, height, owner, new TranslationConverter(x, y), owner.DrawingBoard)
{
    public int Id { get; } = detection.Name.Id;

    public string Name { get; } = detection.Name.Name;

    public float Confidence { get; } = detection.Confidence;
}

public sealed class YoloClassification(Classification classification)
{
    public int Id { get; } = classification.Name.Id;

    public string Name { get; } = classification.Name.Name;

    public float Confidence { get; } = classification.Confidence;
}
