using System;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Compunet.YoloSharp;
using Compunet.YoloSharp.Data;
using Compunet.YoloSharp.Metadata;
using Microsoft.ML.OnnxRuntime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using BetterGenshinImpact.Core.Recognition.Ncnn;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiYoloPredictor : IDisposable
{
    private readonly BgiOnnxModel _model;


    private readonly Lazy<YoloPredictor> _lazyPredictor;
    private readonly SessionOptions _sessionOptions;
    /// <summary>非空时检测（Detect）走 ncnn Vulkan；分类与 Predictor 属性（脚本 API）仍走 ORT。</summary>
    private readonly NcnnYoloPredictor? _ncnnPredictor;
    private readonly object _syncRoot = new();
    private bool _disposed;

    /// <summary>
    /// 使用 BgiOnnxFactory 创建这个类的实例
    /// </summary>
    /// <param name="onnxModel">模型</param>
    /// <param name="modelPath">实际要加载的模型文件的绝对路径，在使用模型缓存的场景下可能有差别</param>
    /// <param name="sessionOptions">sessionOptions</param>
    /// <param name="ncnnPredictor">ncnn 检测后端；为空时检测走 YoloSharp（ORT）</param>
    protected internal BgiYoloPredictor(BgiOnnxModel onnxModel, string modelPath, SessionOptions sessionOptions,
        NcnnYoloPredictor? ncnnPredictor = null)
    {
        _model = onnxModel;
        _sessionOptions = sessionOptions;
        _ncnnPredictor = ncnnPredictor;
        _lazyPredictor = new Lazy<YoloPredictor>(() =>
        {
            try
            {
                return new YoloPredictor(modelPath, new YoloPredictorOptions
                {
                    SessionOptions = _sessionOptions
                });
            }
            finally
            {
                // 会话创建完成（含失败）后不再需要配置；未初始化时由 Dispose 释放。
                _sessionOptions.Dispose();
            }
        });
    }

    public YoloPredictor Predictor
    {
        get
        {
            // 串行化延迟初始化与释放，避免创建会话时配置被提前释放。
            lock (_syncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _lazyPredictor.Value;
            }
        }
    }

    /// <summary>
    /// 检测
    /// </summary>
    /// <param name="region">图像</param>
    /// <returns>类别-矩形框</returns>
    public Dictionary<string, List<Rect>> Detect(ImageRegion region)
    {
        var start = YoloLatencyMonitor.Start();
        var result = DetectCore(region.CacheImage);
        YoloLatencyMonitor.Report(_model.Name, start, result.Count);


        var dict = new Dictionary<string, List<Rect>>();
        foreach (var box in result)
        {
            if (!dict.TryGetValue(box.Name.Name, out var value))
            {
                dict[box.Name.Name] = [new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height)];
            }
            else
            {
                value.Add(new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height));
            }
        }

        Debug.WriteLine("YOLO识别结果:" + JsonSerializer.Serialize(dict));

        var list = result
            .Select(box => new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height))
            .Select(rect => region.ToMaskWindowDrawingRect(rect)).ToList();

        region.DrawingBoard.Set(_model.Name, list);

        return dict;
    }

    /// <summary>
    /// 检测并记录耗时。供直接调用 YoloSharp 的调用方使用，返回原始检测结果。
    /// </summary>
    public YoloResult<Detection> Detect(Image<Rgb24> image)
    {
        var start = YoloLatencyMonitor.Start();
        var result = DetectCore(image);
        YoloLatencyMonitor.Report(_model.Name, start, result.Count);
        return result;
    }

    /// <summary>检测（Detect）当前是否走 ncnn Vulkan 后端；false 时检测走 YoloSharp（ORT）。</summary>
    public bool IsNcnnDetection => _ncnnPredictor is { IsClassifier: false };

    /// <summary>ncnn 开关生效且模型有产物时走 ncnn Vulkan，否则走 YoloSharp（ORT）。</summary>
    private YoloResult<Detection> DetectCore(Image<Rgb24> image)
        => _ncnnPredictor is { IsClassifier: false } ncnn ? ncnn.Detect(image) : Predictor.Detect(image);

    /// <summary>
    /// 分类并记录结果（检测 ncnn 后端不影响分类路径；分类模型有 ncnn 产物时同样走 ncnn）。
    /// 供直接调用 YoloSharp 的调用方使用，返回原始分类结果（全部类别，置信度降序）。
    /// </summary>
    public YoloResult<Classification> Classify(Image<Rgb24> image)
        => _ncnnPredictor is { IsClassifier: true } ncnn ? ncnn.Classify(image) : Predictor.Classify(image);

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_lazyPredictor.IsValueCreated)
                {
                    _lazyPredictor.Value.Dispose();
                }
            }
            finally
            {
                _ncnnPredictor?.Dispose();
                _sessionOptions.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }

    ~BgiYoloPredictor()
    {
        Dispose();
    }
}