using System.Drawing;

namespace BetterGenshinImpact.Core.Recognition.OCR
{
    public interface IOcrService
    {
        public string Ocr(Bitmap bitmap);
    }
}
