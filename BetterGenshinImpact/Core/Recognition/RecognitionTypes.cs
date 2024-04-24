namespace BetterGenshinImpact.Core.Recognition;

public enum RecognitionTypes
{
    None,
    TemplateMatch, // 模板匹配
    ColorMatch, // 颜色匹配
    OcrMatch, // 文字识别并匹配
    Ocr, // 仅文字识别
    Detect
}
