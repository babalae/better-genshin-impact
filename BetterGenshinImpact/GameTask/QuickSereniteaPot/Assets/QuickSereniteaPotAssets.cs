using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot.Assets;

public class QuickSereniteaPotAssets : BaseAssets<QuickSereniteaPotAssets>
{
    public RecognitionObject BagCloseButtonRo;
    public RecognitionObject SereniteaPotIconRo;

    private QuickSereniteaPotAssets()
    {
        var info = TaskContext.Instance().SystemInfo;

        BagCloseButtonRo = new RecognitionObject
        {
            Name = "MapCloseButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapCloseButton.png"),
            RegionOfInterest = new Rect(info.CaptureAreaRect.Width - (int)(107 * info.AssetScale),
                (int)(19 * info.AssetScale),
                (int)(58 * info.AssetScale),
                (int)(58 * info.AssetScale)),
            DrawOnWindow = true
        }.InitTemplate();

        SereniteaPotIconRo = new RecognitionObject
        {
            Name = "SereniteaPotIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickSereniteaPot", "SereniteaPotIcon.png"),
            RegionOfInterest = new Rect(
                (int)(100 * info.AssetScale),
                (int)(100 * info.AssetScale),
                (int)(1190 * info.AssetScale),
                (int)(860 * info.AssetScale)),
            DrawOnWindow = true
        }.InitTemplate();
    }
}
