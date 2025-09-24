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
            RegionOfInterest = new Rect((int)(1610 * AssetScale),(int)(28 * AssetScale),(int)(160 * AssetScale),(int)(45 * AssetScale)),
            Use3Channels = true,
            DrawOnWindow = true
        }.InitTemplate();
    }

}