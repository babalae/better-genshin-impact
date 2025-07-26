using System;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using System.Drawing;
using Vanara.PInvoke;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.QucikBuy.Assets;

public class QuickBuyAssets : BaseAssets<QuickBuyAssets>
{
    private readonly ILogger<QuickBuyAssets> _logger = App.GetLogger<QuickBuyAssets>();

    public RecognitionObject SereniteaPotCoin;

    private QuickBuyAssets()
    {
        SereniteaPotCoin = new RecognitionObject
        {
            Name = "SereniteaPotCoin",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("QucikBuy", "SereniteaPotCoin.png"),
            RegionOfInterest = new Rect((int)(1630 * AssetScale),(int)(30 * AssetScale),(int)(200 * AssetScale),(int)(40 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
    }

}