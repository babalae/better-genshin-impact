using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public class BgiYoloV8PredictorFactory
{
    static Dictionary<string, BgiYoloV8Predictor> _predictors = new();

    public static BgiYoloV8Predictor GetPredictor(string modelRelativePath)
    {
        if (!_predictors.ContainsKey(modelRelativePath))
        {
            _predictors[modelRelativePath] = new BgiYoloV8Predictor(modelRelativePath);
        }

        return _predictors[modelRelativePath];
    }
}