using System;

namespace BetterGenshinImpact.Core.Recognition.ONNX.SVTR;

public class TextInferenceFactory
{
    public static ITextInference Pick { get; } = Create(OcrEngineTypes.YapModel);

    public static ITextInference Create(OcrEngineTypes type)
    {
        return type switch
        {
            OcrEngineTypes.YapModel => new PickTextInference(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
