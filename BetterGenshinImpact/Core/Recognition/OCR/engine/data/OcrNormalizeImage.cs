namespace BetterGenshinImpact.Core.Recognition.OCR;
/// <summary>
/// 标准归一化的三个参数
/// </summary>
public record OcrNormalizeImage(float Scale, float[] Mean, float[] Std);