using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Vision.Recognition.Helper.OCR
{

    public enum OcrEngineType
    {
        WinRT
    }
    public class OcrFactory
    {
        public static IOcrService Create(OcrEngineType type)
        {
            switch (type)
            {
                case OcrEngineType.WinRT:
                    return new MediaOcr();
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }
    }
}
