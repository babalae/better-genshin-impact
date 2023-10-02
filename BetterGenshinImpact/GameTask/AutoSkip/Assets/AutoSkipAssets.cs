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
            RegionOfInterest = new Rect(0, 0, info.GameScreenSize.Width / 5, info.GameScreenSize.Height / 5),
            DrawOnWindow = true
        };
        StopAutoButtonRo.InitTemplate();
        OptionButtonRo = new RecognitionObject
        {
            Name = "OptionButton",
            RecognitionType = RecognitionType.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssertImage("AutoSkip", "option.png"),
            RegionOfInterest = new Rect(info.GameScreenSize.Width / 2, 0, info.GameScreenSize.Width - info.GameScreenSize.Width / 2, info.GameScreenSize.Height),
            DrawOnWindow = false
        };
        OptionButtonRo.InitTemplate();
        MenuRo = new RecognitionObject
        {
            Name = "Menu",
            RecognitionType = RecognitionType.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssertImage("AutoSkip", "menu.png"),
            RegionOfInterest = new Rect(0, 0, info.GameScreenSize.Width / 4, info.GameScreenSize.Height / 4),
            DrawOnWindow = false
        };
        MenuRo.InitTemplate();
    }
}