using BetterGenshinImpact.Core.Recognition.OCR.Engine.data;

namespace BetterGenshinImpact.Core.Recognition.OCR.Engine;

/// <summary>
///     ppocr的版本配置
/// </summary>
public readonly record struct OcrVersionConfig(
    string Name,
    OcrImgMode Mode,
    bool ChannelFirst,
    OcrNormalizeImage NormalizeImage,
    OcrShape Shape)
{
    // 参数来自 https://github.com/PaddlePaddle/PaddleOCR/tree/main/configs/det/PP-OCRv3
    // PP-OCRv3、V4、V5 的参数与 V6 相同，随对应模型移入插件，插件自行定义

    public static OcrVersionConfig PpOcrV6 = new(
        "PP-OCRv6",
        OcrImgMode.BGR,
        false,
        new OcrNormalizeImage(
            1.0f / 255.0f,
            [0.485f, 0.456f, 0.406f],
            [0.229f, 0.224f, 0.225f]
        ), new OcrShape(3, 320, 48));
}
