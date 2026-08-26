using System;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.View.Drawable;
using Compunet.YoloSharp;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiYoloPredictor : IDisposable
{
    private readonly BgiOnnxModel _model;
    private readonly Lazy<YoloPredictor> _lazyPredictor;
    private readonly object _lifecycleLock = new();
    private bool _disposed;

    /// <summary>
    /// 使用 BgiOnnxFactory 创建这个类的实例
    /// </summary>
    /// <param name="onnxModel">模型</param>
    /// <param name="predictorFactory">延迟创建底层预测器，并在工厂内部处理 provider 回退。</param>
    protected internal BgiYoloPredictor(BgiOnnxModel onnxModel, Func<YoloPredictor> predictorFactory)
    {
        _model = onnxModel;
        _lazyPredictor = new Lazy<YoloPredictor>(() =>
        {
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return predictorFactory();
            }
        });
    }

    /// <summary>
    /// 在预测器生命周期锁内执行一次推理，确保 Dispose 不会释放仍在使用的原生会话。
    /// </summary>
    public TResult Run<TResult>(Func<YoloPredictor, TResult> inference)
    {
        ArgumentNullException.ThrowIfNull(inference);
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return inference(_lazyPredictor.Value);
        }
    }

    /// <summary>
    /// 检测
    /// </summary>
    /// <param name="region">图像</param>
    /// <returns>类别-矩形框</returns>
    public Dictionary<string, List<Rect>> Detect(ImageRegion region)
    {
        var result = Run(predictor => predictor.Detect(region.CacheImage));


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
            .Select(rect => region.ToRectDrawable(rect, _model.Name)).ToList();

        VisionContext.Instance().DrawContent.PutOrRemoveRectList(_model.Name, list);

        return dict;
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_lazyPredictor.IsValueCreated)
            {
                _lazyPredictor.Value.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    ~BgiYoloPredictor()
    {
        Dispose();
    }
}
