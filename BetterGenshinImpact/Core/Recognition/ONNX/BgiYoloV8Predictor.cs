using System;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model.Area;
using Compunet.YoloV8;
using OpenCvSharp;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using BetterGenshinImpact.View.Drawable;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiYoloV8Predictor(string modelRelativePath) : IDisposable
{
    private readonly YoloV8Predictor _predictor = YoloV8Builder.CreateDefaultBuilder()
        .UseOnnxModel(Global.Absolute(modelRelativePath))
        .WithSessionOptions(BgiSessionOption.Instance.Options)
        .Build();

    public YoloV8Predictor Predictor => _predictor;

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
        var result = _predictor.Detect(memoryStream);

        var dict = new Dictionary<string, List<Rect>>();
        foreach (var box in result.Boxes)
        {
            if (!dict.ContainsKey(box.Class.Name))
            {
                dict[box.Class.Name] = [new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height)];
            }
            else
            {
                dict[box.Class.Name].Add(new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height));
            }
        }
        Debug.WriteLine("YOLOv8识别结果:" + JsonSerializer.Serialize(dict));
        
        var list = new List<RectDrawable>();
        foreach (var box in result.Boxes)
        {
            var rect = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
            list.Add(region.ToRectDrawable(rect, modelRelativePath));
        }

        VisionContext.Instance().DrawContent.PutOrRemoveRectList(modelRelativePath, list);
        
        return dict;
    }

    public void Dispose()
    {
        _predictor.Dispose();
    }
}
