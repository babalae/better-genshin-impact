using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Runtime;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.BgiVision;

public class BvImage
{
    public RecognitionObject RecognitionObject { get; }
    
    /// <summary>
    /// 根据素材名创建模板匹配对象
    /// </summary>
    /// <param name="runtime">运行环境，用于按当前游戏分辨率加载素材</param>
    /// <param name="templateAssert">素材名，格式为 "功能名:文件名"</param>
    /// <param name="roi">感兴趣区域</param>
    /// <param name="threshold">匹配阈值</param>
    public BvImage(GameRuntime runtime, string templateAssert, Rect roi = default, double threshold = 0.8)
    {
        var args = templateAssert.Split(':');
        var featureName = args[0];
        var assertName = args[1];

        RecognitionObject = new RecognitionObject
        {
            Name = templateAssert,
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(featureName, assertName, runtime.SystemInfo),
            RegionOfInterest = roi,
            Threshold = threshold
        }.InitTemplate();
    }

    public RecognitionObject ToRecognitionObject()
    {
        return RecognitionObject;
    }
}
