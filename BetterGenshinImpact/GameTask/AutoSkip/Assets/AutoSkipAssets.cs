using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoSkip.Assets;

public class AutoSkipAssets
{
    public RecognitionObject StopAutoButtonRo;
    public RecognitionObject OptionButtonRo;
    public RecognitionObject MenuRo;

    public AutoSkipAssets()
    {
        var info = TaskContext.Instance().SystemInfo;
        StopAutoButtonRo = new RecognitionObject
        {
            Name = "StopAutoButton",
            RecognitionType = RecognitionType.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssertImage("AutoSkip", "stop_auto.png"),
            RegionOfInterest = new Rect(0, 0, info.CaptureAreaRect.Width / 5, info.CaptureAreaRect.Height / 5),
            DrawOnWindow = true
        };
        StopAutoButtonRo.InitTemplate();
        OptionButtonRo = new RecognitionObject
        {
            Name = "OptionButton",
            RecognitionType = RecognitionType.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssertImage("AutoSkip", "option.png"),
            RegionOfInterest = new Rect(info.CaptureAreaRect.Width / 2, 0, info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 2, info.CaptureAreaRect.Height),
            DrawOnWindow = false
        };
        OptionButtonRo.InitTemplate();
        MenuRo = new RecognitionObject
        {
            Name = "Menu",
            RecognitionType = RecognitionType.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssertImage("AutoSkip", "menu.png"),
            RegionOfInterest = new Rect(0, 0, info.CaptureAreaRect.Width / 4, info.CaptureAreaRect.Height / 4),
            DrawOnWindow = false
        };
        MenuRo.InitTemplate();
    }
}