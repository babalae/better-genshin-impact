using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.QuickBuy.Assets;

public class QuickBuyAssets : BaseAssets<QuickBuyAssets>
{
    public RecognitionObject SereniteaPotCoin;

    private QuickBuyAssets()
    {
        SereniteaPotCoin = new RecognitionObject
        {
            Name = "SereniteaPotCoin",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QuickBuy", "SereniteaPotCoin.png"),
            RegionOfInterest = new Rect((int)(1620 * AssetScale),(int)(30 * AssetScale),(int)(50 * AssetScale),(int)(40 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
    }

}