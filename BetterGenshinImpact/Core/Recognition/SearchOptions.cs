using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition;

public class SearchOptions
{
    /// <summary>
    /// 搜索锚定点。当输入图与参考图宽高比不一致时，决定缩放后的参考画布贴向哪一侧。
    /// 这个缩放规则需要和待匹配 UI 在不同分辨率下的布局缩放规则保持一致。
    /// </summary>
    public SearchAnchorMode AnchorMode { get; set; } = SearchAnchorMode.Auto;

    /// <summary>
    /// 在预测框外额外扩展的像素大小。未指定时默认四周各扩展 10px。
    /// </summary>
    public Size? ExpandSize { get; set; }
}

/// <summary>
/// 参考搜索的锚定方式。
/// 元素在画布右/下侧时通常使用右/下锚定，元素在左/上侧时通常使用左/上锚定；
/// 居中元素使用中心锚定，按画布中心加偏移进行缩放。
/// </summary>
public enum SearchAnchorMode
{
    Auto,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center
}
