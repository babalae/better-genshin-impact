using System;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public class OcrFactory
{
    // public static IOcrService Media = Create(OcrEngineTypes.Media);
    public static IOcrService Paddle { get; } = Create(OcrEngineTypes.Paddle);

    public static IOcrService Create(OcrEngineTypes type)
    {
        return type switch
        {
            OcrEngineTypes.Paddle => new PaddleOcrService(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
