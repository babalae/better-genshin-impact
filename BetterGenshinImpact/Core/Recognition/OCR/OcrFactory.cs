using System;

namespace BetterGenshinImpact.Core.Recognition.OCR
{

    public enum OcrEngineType
    {
        WinRT
    }
    public class OcrFactory
    {
        public static BetterGenshinImpact.Core.Recognition.OCR.IOcrService Create(OcrEngineType type)
        {
            switch (type)
            {
                case OcrEngineType.WinRT:
                    return new BetterGenshinImpact.Core.Recognition.OCR.MediaOcr();
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}
