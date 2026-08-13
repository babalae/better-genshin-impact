using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR;

/// <summary>
///     单次 OCR 检测参数覆盖。
/// </summary>
public readonly record struct OcrDetectionOptions(float? UnclipRatio = null);

public interface IOcrService
{
    public string Ocr(Mat mat);

    public string OcrWithoutDetector(Mat mat);

    public OcrResult OcrResult(Mat mat, OcrDetectionOptions? detectionOptions = null);
}
