using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;
using BetterGenshinImpact.View.Drawable;
using Compunet.YoloV8;
using Compunet.YoloV8.Data;

namespace BetterGenshinImpact.GameTask.Placeholder;

/// <summary>
/// 一个用于开发测试的识别、或者全局占位触发器
/// 这个触发器启动的时候，直接独占
/// </summary>
public class TestTrigger : ITaskTrigger
{
    public string Name => "自定义占位触发器";
    public bool IsEnabled { get; set; }
    public int Priority => 9999;
    public bool IsExclusive { get; private set; }

    private readonly Pen _pen = new(Color.Coral, 1);

    //private readonly AutoGeniusInvokationAssets _autoGeniusInvokationAssets;

    //private readonly YoloV8 _predictor = new(Global.Absolute("Config\\Model\\Fish\\bgi_fish.onnx"));

    public TestTrigger()
    {
        var info = TaskContext.Instance().SystemInfo;
        //_autoGeniusInvokationAssets = new AutoGeniusInvokationAssets();
    }

    public void Init()
    {
        IsEnabled = false;
        IsExclusive = true;
    }

    public void OnCapture(CaptureContent content)
    {
        //Detect(content);


        //var dictionary = GeniusInvokationControl.FindMultiPicFromOneImage2OneByOne(content.CaptureRectArea.SrcGreyMat, _autoGeniusInvokationAssets.RollPhaseDiceMats, 0.7);
        //if (dictionary.Count > 0)
        //{
        //    int i = 0;
        //    foreach (var pair in dictionary)
        //    {
        //        var list = pair.Value;

        //        foreach (var p in list)
        //        {
        //            i++;
        //            VisionContext.Instance().DrawContent.PutRect("i" + i,
        //                new RectDrawable(new Rect(p.X, p.Y,
        //                    _autoGeniusInvokationAssets.RollPhaseDiceMats[pair.Key].Width,
        //                    _autoGeniusInvokationAssets.RollPhaseDiceMats[pair.Key].Height)));
        //        }
        //    }
        //    Debug.WriteLine("找到了" + i + "个");
        //}


        //var foundRectArea = content.CaptureRectArea.Find(_autoGeniusInvokationAssets.ElementalTuningConfirmButtonRo);
        //if (!foundRectArea.IsEmpty())
        //{
        //    Debug.WriteLine("找到了");
        //}
        //else
        //{
        //    Debug.WriteLine("没找到");
        //}
    }

    //private void Detect(CaptureContent content)
    //{
    //    using var memoryStream = new MemoryStream();
    //    content.CaptureRectArea.SrcBitmap.Save(memoryStream, ImageFormat.Bmp);
    //    memoryStream.Seek(0, SeekOrigin.Begin);
    //    var result = _predictor.Detect(memoryStream);
    //    Debug.WriteLine(result);
    //    var list = new List<RectDrawable>();
    //    foreach (var box in result.Boxes)
    //    {
    //        var rect = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
    //        list.Add(new RectDrawable(rect, _pen));
    //    }

    //    VisionContext.Instance().DrawContent.PutOrRemoveRectList("Box", list);
    //}
}