using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.View.Drawable;
using Compunet.YoloSharp;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiYoloPredictor : IDisposable
{
    private readonly string _modelRelativePath;


    private readonly Lazy<YoloPredictor> _lazyPredictor;

    /// <summary>
    /// 使用 BgiOnnxFactory 创建这个类的实例
    /// </summary>
    /// <param name="modelRelativePath">模型相对路径</param>
    protected internal BgiYoloPredictor(string modelRelativePath)
    {
        _modelRelativePath = modelRelativePath;
        _lazyPredictor = new Lazy<YoloPredictor>(() => new YoloPredictor(_modelRelativePath,
            new YoloPredictorOptions
            {
                SessionOptions = BgiSessionOptionBuilder.Instance.BuildWithRelativePath(_modelRelativePath)
            }));
    }

    public YoloPredictor Predictor => _lazyPredictor.Value;

    /// <summary>
    /// 检测
    /// </summary>
    /// <param name="region">图像</param>
    /// <returns>类别-矩形框</returns>
    public Dictionary<string, List<Rect>> Detect(ImageRegion region)
    {
        using var memoryStream = new MemoryStream();
        region.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
        memoryStream.Seek(0, SeekOrigin.Begin);
        var result = Predictor.Detect(memoryStream);


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
            .Select(rect => region.ToRectDrawable(rect, _modelRelativePath)).ToList();

        VisionContext.Instance().DrawContent.PutOrRemoveRectList(_modelRelativePath, list);

        return dict;
    }

    public void Dispose()
    {
        if (_lazyPredictor.IsValueCreated)
        {
            Predictor.Dispose();
        }
    }
}