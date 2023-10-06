using System;
using BetterGenshinImpact.Core.Recognition.OCR;

namespace BetterGenshinImpact.Core.Recognition.ONNX.SVTR
{
    public class TextInferenceFactory
    {
        public static ITextInference Create(OcrEngineTypes type)
        {
            switch (type)
            {
                case OcrEngineTypes.YapModel:
                    return new  PickTextInference();
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public static ITextInference Pick = Create(OcrEngineTypes.YapModel);
    }
}
