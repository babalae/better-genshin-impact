using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public interface IOcrService
{
    public string Ocr(Mat mat);

    public string OcrWithoutDetector(Mat mat);

    /// <summary>
    ///     不经过文字检测，按输入顺序识别多张已裁剪好的单行文字图像，返回文字和置信度。
    ///     调用方保留图像所有权。
    /// </summary>
    public OcrRecognizerResult[] OcrLinesWithoutDetector(Mat[] lines);

    public OcrResult OcrResult(Mat mat);
}
