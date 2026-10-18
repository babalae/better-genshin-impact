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

    /// <summary>
    /// 按运行环境的画面尺寸获取识别对象。手头有整帧截图时优先使用 <see cref="Get(string, Region)"/>
    /// </summary>
    public static RecognitionObject Get(string objectName, ISystemInfo systemInfo)
    {
        return RecognitionAssets.Get(@"Common\Element", objectName, systemInfo);
    }

    /// <summary>
    /// 聊天界面左上角的返回按钮。素材尺寸按截图帧推导
    /// </summary>
    /// <param name="region">截图区域，用于推导截图帧的尺寸</param>
    public static RecognitionObject GetChatBackButton(Region region)
    {
        var assetScale = region.GetAssetScale();
        var frameSize = region.GetScaleMax1080PFrameSize();
        return new RecognitionObject
        {
            Name = "ChatBackButton",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(@"UseRedeemCode", "esc_return_button.png", frameSize.Width, frameSize.Height),
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
