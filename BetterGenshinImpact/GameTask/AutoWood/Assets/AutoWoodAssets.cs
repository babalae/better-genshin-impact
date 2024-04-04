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
        var info = TaskContext.Instance().SystemInfo;
        //「王树瑞佑」
        TheBoonOfTheElderTreeRo = new RecognitionObject
        {
            Name = "TheBoonOfTheElderTree",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "TheBoonOfTheElderTree.png"),
            RegionOfInterest = new Rect(info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 4, info.CaptureAreaRect.Height / 2,
                info.CaptureAreaRect.Width / 4, info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();

        // CharacterGuideRo = new RecognitionObject
        // {
        //     Name = "CharacterGuide",
        //     RecognitionType = RecognitionTypes.TemplateMatch,
        //     TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "character_guide.png"),
        //     RegionOfInterest = new Rect(0, 0, info.CaptureAreaRect.Width / 2, info.CaptureAreaRect.Height),
        //     DrawOnWindow = false
        // }.InitTemplate();

        MenuBagRo = new RecognitionObject
        {
            Name = "MenuBag",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoWood", "menu_bag.png"),
            RegionOfInterest = new Rect(0, 0, info.CaptureAreaRect.Width / 2, info.CaptureAreaRect.Height),
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
            RegionOfInterest = new Rect(0, info.CaptureAreaRect.Height / 2, info.CaptureAreaRect.Width, info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 2),
            DrawOnWindow = false
        }.InitTemplate();
    }
}
