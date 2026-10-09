using System.Windows.Media;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 遮罩窗口绘制层的全局样式，由 MaskWindowViewModel 从 MaskWindowConfig 的 Recognition* 配置合成
/// </summary>
/// <param name="OverrideRecognitionStroke">为 true 时 Recognition 分组忽略图形自带的线条样式，统一使用下面的线条样式</param>
/// <param name="RectStroke">识别框的统一线条样式</param>
/// <param name="LineStroke">识别线的统一线条样式</param>
/// <param name="TextColor">文字未指定颜色时使用的颜色</param>
/// <param name="TextFontSize">文字未指定字号时使用的字号，单位为捕获像素</param>
public sealed record MaskWindowDrawingStyle(
    bool OverrideRecognitionStroke,
    MaskWindowDrawingStroke RectStroke,
    MaskWindowDrawingStroke LineStroke,
    Color TextColor,
    double TextFontSize)
{
    public static MaskWindowDrawingStroke DefaultStroke { get; } = new(Colors.Red, 2);

    public static MaskWindowDrawingStyle Default { get; } =
        new(false, DefaultStroke, DefaultStroke, Colors.Black, 36);
}
