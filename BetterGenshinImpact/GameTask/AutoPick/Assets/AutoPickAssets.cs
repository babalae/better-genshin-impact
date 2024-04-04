using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;
using System.Drawing;

namespace BetterGenshinImpact.GameTask.AutoPick.Assets;

public class AutoPickAssets : BaseAssets<AutoPickAssets>
{
    public RecognitionObject FRo;
    public RecognitionObject ChatIconRo;
    public RecognitionObject SettingsIconRo;

    private AutoPickAssets()
    {
        FRo = new RecognitionObject
        {
            Name = "F",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoPick", "F.png"),
            RegionOfInterest = new Rect((int)(1090 * AssetScale),
                (int)(330 * AssetScale),
                (int)(60 * AssetScale),
                (int)(420 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();

        ChatIconRo = new RecognitionObject
        {
            Name = "ChatIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoSkip", "icon_option.png"),
            DrawOnWindow = false,
            DrawOnWindowPen = new Pen(Color.Chocolate, 2)
        }.InitTemplate();
        SettingsIconRo = new RecognitionObject
        {
            Name = "SettingsIcon",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoPick", "icon_settings.png"),
            DrawOnWindow = false,
            DrawOnWindowPen = new Pen(Color.Chocolate, 2)
        }.InitTemplate();
    }
}
