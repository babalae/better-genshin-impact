using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.QuickClaimReward.Assets;

public class QuickClaimRewardAssets : BaseAssets<QuickClaimRewardAssets>
{
    public RecognitionObject ClaimTextRo;
    public RecognitionObject ClaimGiftRo;
    public RecognitionObject ClickBlankContinueRo;

    private QuickClaimRewardAssets()
    {
        var fullScreen = new Rect(0, 0, CaptureRect.Width, CaptureRect.Height);

        ClaimTextRo = new RecognitionObject
        {
            Name = "ClaimRewardText",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickClaimReward", "claim_text.png"),
            RegionOfInterest = fullScreen,
            Threshold = 0.86,
            MaxMatchCount = 20,
            DrawOnWindow = true
        }.InitTemplate();

        ClaimGiftRo = new RecognitionObject
        {
            Name = "ClaimRewardGift",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickClaimReward", "claim_gift.png"),
            RegionOfInterest = fullScreen,
            Threshold = 0.86,
            MaxMatchCount = 20,
            DrawOnWindow = true
        }.InitTemplate();

        ClickBlankContinueRo = new RecognitionObject
        {
            Name = "ClickBlankContinue",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickClaimReward", "click_blank_continue.png"),
            RegionOfInterest = fullScreen,
            Threshold = 0.82,
            DrawOnWindow = false
        }.InitTemplate();
    }
}
