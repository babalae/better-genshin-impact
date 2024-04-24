using OpenCvSharp;
using Sdcb.PaddleOCR;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public interface IOcrService
{
    public string Ocr(Mat mat);

    public string OcrWithoutDetector(Mat mat);

    public PaddleOcrResult OcrResult(Mat mat);
}
