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

public sealed class YoloDetection(string rootPath)
{
    /// <summary>
    /// 创建基于 ONNX 模型的 YOLO 检测器
    /// </summary>
    /// <param name="modelPath">相对于脚本工程根目录的模型路径</param>
    /// <param name="options">可选检测配置；未指定的属性使用 <see cref="YoloConfiguration"/> 默认值</param>
    /// <remarks>
    /// <paramref name="options"/> 使用 <see cref="YoloConfiguration"/> 的属性名，作为检测器的基准配置
    /// 支持以下常用项：
    /// <list type="bullet">
    ///   <item><description><c>confidence</c>：置信度阈值（默认 0.3）</description></item>
    ///   <item><description><c>iou</c>：NMS 的 IoU 阈值（默认 0.45）</description></item>
    ///   <item><description><c>keepAspectRatio</c>：是否保持宽高比并留边（默认 true）</description></item>
    ///   <item><description><c>applyAutoOrient</c>：是否根据图像方向标记校正图像（默认 true）/></description></item>
    ///   <item><description><c>suppressParallelInference</c>：是否等待并发推理（默认 false）</description></item>
    /// </list>
    /// </remarks>
    public YoloDetector CreatePredictor(string modelPath, ScriptObject? options = null)
    {
        modelPath = ScriptUtils.NormalizePath(rootPath, modelPath);
        if (!modelPath.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("模型文件必须是.onnx");
        }

        var configuration = MergeConfiguration(YoloConfiguration.Default, options);
        return new YoloDetector(modelPath, configuration);
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

public sealed class YoloDetector : IDisposable
{
    private readonly object _gate = new();
    private readonly YoloConfiguration _configuration;
    private readonly string _modelPath;
    private BgiYoloPredictor? _predictor;
    private bool _disposed;

    internal YoloDetector(string modelPath, YoloConfiguration configuration)
    {
        _modelPath = modelPath;
        _configuration = configuration;
    }

    /// <summary>
    /// 对当前游戏画面做检测（可选择使用自定义检测参数）
    /// </summary>
    public List<YoloRegion> Detect(ScriptObject? options = null)
    {
        return Run(options is null ? _configuration : YoloDetection.MergeConfiguration(_configuration, options));
    }

    private List<YoloRegion> Run(YoloConfiguration configuration)
    {
        using var ra = TaskControl.CaptureToRectArea();
        return GetPredictor().Detect(ra.CacheImage, configuration).Select(box =>
        {
            var bounds = box.Bounds;
            return new YoloRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height, box, ra);
        }).ToList();
    }

    private YoloPredictor GetPredictor()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return (_predictor ??= CreatePredictor()).Predictor;
        }
    }

    private BgiYoloPredictor CreatePredictor()
    {
        var cacheRelativePath = Path.Combine(BgiOnnxModel.ModelCacheRelativePath, "js",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_modelPath))));
        var model = new BgiOnnxModel(Path.GetFileNameWithoutExtension(_modelPath), _modelPath, cacheRelativePath);
        var predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(model);
        try
        {
            var yolo = predictor.Predictor;
            if (yolo.Metadata.Task != YoloTask.Detect || yolo.Metadata.Names.Length == 0)
            {
                throw new InvalidOperationException("模型必须是类别名非空的检测模型");
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
            _predictor?.Dispose();
            _predictor = null;
        }
    }
}

public class YoloRegion(int x, int y, int width, int height, YoloPrediction detection, Region owner)
    : Region(x, y, width, height, owner, new TranslationConverter(x, y), owner.DrawingBoard)
{
    public YoloPrediction Detection { get; } = detection;
}