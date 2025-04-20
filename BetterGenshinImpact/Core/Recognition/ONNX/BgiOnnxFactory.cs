using System.IO;
using BetterGenshinImpact.Core.Config;
using Microsoft.ML.OnnxRuntime;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public static class BgiOnnxFactory
{
    public static BgiYoloPredictor CreateYoloPredictor(string modelRelativePath)
    {
        return new BgiYoloPredictor(modelRelativePath);
    }

    public static InferenceSession CreateInferenceSession(string modelRelativePath)
    {
        return new InferenceSession(Global.Absolute(modelRelativePath),
            BgiSessionOptionBuilder.Instance.BuildWithRelativePath(modelRelativePath));
    }
    
    public static bool IsModelExist(string modelRelativePath)
    {
        return File.Exists(Global.Absolute(modelRelativePath));
    }
}