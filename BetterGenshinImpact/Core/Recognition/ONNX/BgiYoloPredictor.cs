using System;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Compunet.YoloSharp;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiYoloPredictor : IDisposable
{
    private readonly BgiOnnxModel _model;


    private readonly Lazy<YoloPredictor> _lazyPredictor;
    private readonly SessionOptions _sessionOptions;
    private readonly object _syncRoot = new();
    private bool _disposed;

    /// <summary>
    /// 使用 BgiOnnxFactory 创建这个类的实例
    /// </summary>
    /// <param name="onnxModel">模型</param>
    /// <param name="modelPath">实际要加载的模型文件的绝对路径，在使用模型缓存的场景下可能有差别</param>
    /// <param name="sessionOptions">sessionOptions</param>
    protected internal BgiYoloPredictor(BgiOnnxModel onnxModel, string modelPath, SessionOptions sessionOptions)
    {
        _model = onnxModel;
        _sessionOptions = sessionOptions;
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
        var result = Predictor.Detect(region.CacheImage);


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