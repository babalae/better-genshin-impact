using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoWood.Assets;

public class AutoWoodAssets : BaseAssets<AutoWoodAssets>
{
    public RecognitionObject TheBoonOfTheElderTreeRo;

    // public RecognitionObject CharacterGuideRo;
    public RecognitionObject MenuBagRo;

    public RecognitionObject ConfirmRo;
    public RecognitionObject EnterGameRo;

    private AutoWoodAssets()
    {
        //「王树瑞佑」
        TheBoonOfTheElderTreeRo = new RecognitionObject
        {
            Name = "TheBoonOfTheElderTree",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "TheBoonOfTheElderTree.png"),
            RegionOfInterest = new Rect(CaptureRect.Width - CaptureRect.Width / 4, CaptureRect.Height / 2,
                CaptureRect.Width / 4, CaptureRect.Height - CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        // CharacterGuideRo = new RecognitionObject
        // {
        //     Name = "CharacterGuide",
        //     RecognitionType = RecognitionTypes.TemplateMatch,
        //     TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "character_guide.png"),
        //     RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 2, CaptureRect.Height),
        //     DrawOnWindow = false
        // }.InitTemplate();

        MenuBagRo = new RecognitionObject
        {
            Name = "MenuBag",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "menu_bag.png"),
            RegionOfInterest = new Rect(0, 0, CaptureRect.Width / 2, CaptureRect.Height),
            DrawOnWindow = false
        }.InitTemplate();

        ConfirmRo = new RecognitionObject
        {
            Name = "AutoWoodConfirm",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "confirm.png"),
            DrawOnWindow = false
        }.InitTemplate();

        EnterGameRo = new RecognitionObject
        {
            Name = "EnterGame",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "exit_welcome.png"),
            RegionOfInterest = new Rect(0, CaptureRect.Height / 2, CaptureRect.Width, CaptureRect.Height - CaptureRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();
    }
}
