using System.Collections.Generic;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public static class ElementRecognition
{
    public static RecognitionObject Get(string objectName, Region region)
    {
        return RecognitionAssets.Get(@"Common\Element", objectName, region);
    }

    public static RecognitionObject Get(string objectName, int captureWidth, int captureHeight)
    {
        return RecognitionAssets.Get(@"Common\Element", objectName, captureWidth, captureHeight);
    }

    public static RecognitionObject Get(string objectName)
    {
        return RecognitionAssets.Get(@"Common\Element", objectName);
    }

    public static RecognitionObject GetChatBackButton()
    {
        var systemInfo = TaskContext.Instance().SystemInfo;
        var assetScale = systemInfo.AssetScale;
        return new RecognitionObject
        {
            Name = "ChatBackButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"UseRedeemCode", "esc_return_button.png", systemInfo),
            RegionOfInterest = new Rect(0, 0, (int)(220 * assetScale), (int)(160 * assetScale)),
            Threshold = 0.72,
            DrawOnWindow = false
        }.InitTemplate();
    }

    public static IReadOnlyList<RecognitionObject> GetIndexList(Region region)
    {
        return
        [
            Get("Index1", region),
            Get("Index2", region),
            Get("Index3", region),
            Get("Index4", region),
        ];
    }
}
