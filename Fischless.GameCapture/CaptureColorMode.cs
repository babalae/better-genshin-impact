namespace Fischless.GameCapture;

/// <summary>
/// 描述截图器实际输出帧所经过的色彩管线。
/// </summary>
public enum CaptureColorMode
{
    /// <summary>
    /// 直接输出标准动态范围的 8-bit BGR 图像。
    /// </summary>
    Sdr = 0,

    /// <summary>
    /// 以 FP16 scRGB 捕获 HDR 源，并在 GPU 上归一化为供现有识别器使用的 8-bit BGR 图像。
    /// </summary>
    HdrToSdr = 1,
}
