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
        BagCloseButtonRo = new RecognitionObject
        {
            Name = "MapCloseButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickTeleport", "MapCloseButton.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - (int)(107 * AssetScale),
                (int)(19 * AssetScale),
                (int)(58 * AssetScale),
                (int)(58 * AssetScale)),
            DrawOnWindow = true
        }.InitTemplate();

        SereniteaPotIconRo = new RecognitionObject
        {
            Name = "SereniteaPotIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickSereniteaPot", "SereniteaPotIcon.png"),
            RegionOfInterest = new Rect(
                (int)(100 * AssetScale),
                (int)(100 * AssetScale),
                (int)(1190 * AssetScale),
                (int)(860 * AssetScale)),
            DrawOnWindow = true
        }.InitTemplate();
    }
}
