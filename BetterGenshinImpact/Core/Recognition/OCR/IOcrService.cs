using System.Drawing;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR
{
    public interface IOcrService
    {
        public string Ocr(Bitmap bitmap);

        public string Ocr(Mat mat);
    }
}
