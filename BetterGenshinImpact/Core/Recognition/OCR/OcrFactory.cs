using System;

namespace BetterGenshinImpact.Core.Recognition.OCR
{
    public class OcrFactory
    {
        public static IOcrService Create(OcrEngineTypes type)
        {
            switch (type)
            {
                case OcrEngineTypes.Media:
                    return new MediaOcrService();
                case OcrEngineTypes.Paddle:
                    return new PaddleOcrService();
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public static IOcrService Media = Create(OcrEngineTypes.Media);
        public static IOcrService Paddle = Create(OcrEngineTypes.Paddle);
    }
}
