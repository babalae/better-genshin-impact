using System;

namespace BetterGenshinImpact.Core.Recognition.OCR
{
    public class OcrFactory
    {
        public static IOcrService Create(OcrEngineTypes type)
        {
            switch (type)
            {
                case OcrEngineTypes.MediaOcr:
                    return new BetterGenshinImpact.Core.Recognition.OCR.MediaOcr();
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public static IOcrService MediaOcr = Create(OcrEngineTypes.MediaOcr);
    }
}
