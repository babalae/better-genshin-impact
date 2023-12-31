using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFight.Assets;

public class AutoFightAssets
{
    public Rect TeamRect;
    public Rect ERect;
    public Rect QRect;
    public RecognitionObject WandererIconRa;


    public AutoFightAssets()
    {
        var info = TaskContext.Instance().SystemInfo;
        var captureRect = info.CaptureAreaRect;
        var assetScale = info.AssetScale;

        TeamRect = new Rect(captureRect.Width - (int)(355 * assetScale), (int)(220 * assetScale),
            (int)(355 * assetScale), (int)(465 * assetScale));
        ERect = new Rect(captureRect.Width - (int)(267 * assetScale), captureRect.Height - (int)(132 * assetScale),
            (int)(77 * assetScale), (int)(77 * assetScale));
        QRect = new Rect(captureRect.Width - (int)(157 * assetScale), captureRect.Height - (int)(165 * assetScale),
            (int)(110 * assetScale), (int)(110 * assetScale));

        WandererIconRa = new RecognitionObject
        {
            Name = "WandererIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoFight", "wanderer_icon.png"), 
            DrawOnWindow = false
        }.InitTemplate();
    }   
}