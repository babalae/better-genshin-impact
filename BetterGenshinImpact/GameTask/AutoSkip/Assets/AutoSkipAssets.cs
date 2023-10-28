using System;
using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoSkip.Assets;

public class AutoSkipAssets
{
    public RecognitionObject StopAutoButtonRo;
    public RecognitionObject OptionButtonRo;
    public RecognitionObject MenuRo;

    public Mat BinaryStopAutoButtonMat;

    public AutoSkipAssets()
    {
        var info = TaskContext.Instance().SystemInfo;
        StopAutoButtonRo = new RecognitionObject
        {
            Name = "StopAutoButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoSkip", "stop_auto.png"),
            RegionOfInterest = new Rect(0, 0, info.CaptureAreaRect.Width / 5, info.CaptureAreaRect.Height / 5),
            DrawOnWindow = true
        }.InitTemplate();

        // 二值化的跳过剧情按钮
        if (StopAutoButtonRo.TemplateImageGreyMat == null)
        {
            throw new Exception("StopAutoButtonRo.TemplateImageGreyMat == null");
        }
        BinaryStopAutoButtonMat = StopAutoButtonRo.TemplateImageGreyMat.Clone();


        Cv2.Threshold(BinaryStopAutoButtonMat, BinaryStopAutoButtonMat, 0, 255, ThresholdTypes.BinaryInv);
        OptionButtonRo = new RecognitionObject
        {
            Name = "OptionButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoSkip", "option.png"),
            RegionOfInterest = new Rect(info.CaptureAreaRect.Width / 2, 0, info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 2, info.CaptureAreaRect.Height),
            DrawOnWindow = false
        }.InitTemplate();
        MenuRo = new RecognitionObject
        {
            Name = "Menu",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoSkip", "menu.png"),
            RegionOfInterest = new Rect(0, 0, info.CaptureAreaRect.Width / 4, info.CaptureAreaRect.Height / 4),
            DrawOnWindow = false
        }.InitTemplate();
    }
}