using System.Drawing;
using OpenCvSharp;
using Sdcb.PaddleOCR;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public interface IOcrService
{
    public string Ocr(Mat mat);

    public PaddleOcrResult OcrResult(Mat mat);
}