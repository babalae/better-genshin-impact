using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.GameLoading.Assets;

public class GameLoadingAssets
{
    public RecognitionObject EnterGameRo;
    public RecognitionObject WelkinMoonRo;

    public GameLoadingAssets()
    {
        var info = TaskContext.Instance().SystemInfo;

        EnterGameRo = new RecognitionObject
        {
            Name = "EnterGame",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "exit_welcome.png"),
            RegionOfInterest = new Rect(0, info.CaptureAreaRect.Height / 2, info.CaptureAreaRect.Width, info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        WelkinMoonRo = new RecognitionObject
        {
            Name = "WelkinMoon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("GameLoading", "welkin_moon_logo.png"),
            RegionOfInterest = new Rect(0, info.CaptureAreaRect.Height / 2, info.CaptureAreaRect.Width, info.CaptureAreaRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();
    }
}