using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.BgiVision;

public class BvImage
{
    public RecognitionObject RecognitionObject { get; }
    
    public BvImage(string templateAssert, Rect roi = default, double threshold = 0.8)
    {
        var args = templateAssert.Split(':');
        var featureName = args[0];
        var assertName = args[1];

        RecognitionObject = new RecognitionObject
        {
            Name = templateAssert,
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage(featureName, assertName),
            RegionOfInterest = roi,
            Threshold = threshold
        }.InitTemplate();
    }

    public RecognitionObject ToRecognitionObject()
    {
        return RecognitionObject;
    }
}