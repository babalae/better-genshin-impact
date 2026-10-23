using OpenCvSharp;

namespace OcrDmlProbe.Ocr;

public enum OcrImgMode
{
    BGR,
    RGB
}

public record OcrNormalizeImage(float Scale, float[] Mean, float[] Std);

public readonly record struct OcrShape(int Channel, int Width, int Height);

/// <summary>ppocr的版本配置（与 BetterGI OcrVersionConfig 一致，仅保留 V6）</summary>
public readonly record struct OcrVersionConfig(
    string Name,
    OcrImgMode Mode,
    bool ChannelFirst,
    OcrNormalizeImage NormalizeImage,
    OcrShape Shape)
{
    public static readonly OcrVersionConfig PpOcrV6 = new(
        "PP-OCRv6",
        OcrImgMode.BGR,
        false,
        new OcrNormalizeImage(
            1.0f / 255.0f,
            [0.485f, 0.456f, 0.406f],
            [0.229f, 0.224f, 0.225f]
        ), new OcrShape(3, 320, 48));
}

public record OcrResultRegion(RotatedRect Rect, string Text, float Score);

public record OcrResult(OcrResultRegion[] Regions)
{
    public string Text => string.Join("\n", Regions.Select(r => r.Text));
}

public record OcrRecognizerResult(string Text, float Score);

/// <summary>单次 Det 阶段耗时（毫秒）</summary>
public record DetTimings(double ResizeMs, double NormMs, double InferMs, double OutConvMs, double PostMs, int Boxes)
{
    public double TotalMs => ResizeMs + NormMs + InferMs + OutConvMs + PostMs;
}

/// <summary>单批 Rec 阶段耗时（毫秒）</summary>
public record RecTimings(double PreMs, double InferMs, double DecodeMs, int Lines, int BucketWidth);
