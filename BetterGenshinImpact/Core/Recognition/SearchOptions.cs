using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition;

/// <summary>
/// 参考画布锚定搜索的运行时选项，控制基础搜索框、响应式锚点以及最终区域的扩展方式。
/// </summary>
public class SearchOptions
{
    /// <summary>
    /// 搜索锚定点。当输入图与参考图宽高比不一致时，决定缩放后的参考画布贴向哪一侧。
    /// 这个缩放规则需要和待匹配 UI 在不同分辨率下的布局缩放规则保持一致。
    /// </summary>
    public SearchAnchorMode AnchorMode { get; set; } = SearchAnchorMode.Auto;

    /// <summary>
    /// 参考画布坐标系中的独立搜索框。
    /// 未指定时以 <see cref="RecognitionObject.ReferenceBoundingBox"/> 作为基础搜索框；
    /// 指定后会与参考包围盒使用相同的缩放和锚定规则转换到当前截图坐标系。
    /// </summary>
    public Rect? ReferenceSearchBox { get; set; }

    /// <summary>
    /// 在基础搜索框外额外扩展的像素大小，Width 用于左右，Height 用于上下。
    /// 未指定时默认四周各扩展 10px。
    /// 当 <see cref="ExpandPercent"/> 有值时，本属性不生效。
    /// </summary>
    public Size? ExpandSize { get; set; }

    /// <summary>
    /// 按当前截图宽高计算的四边扩展比例。
    /// 左右分别乘当前截图宽度，上下分别乘当前截图高度；例如 0.05 表示 5%。
    /// 本属性优先于 <see cref="ExpandSize"/>，显式设置全零比例可关闭默认的 10px 扩展。
    /// </summary>
    public SearchExpandRatio? ExpandPercent { get; set; }
}

/// <summary>
/// 搜索区域四边的扩展比例，属性顺序与 XAML <c>Thickness</c> 的四参数顺序一致：
/// Left、Top、Right、Bottom。所有值均为直接参与计算的小数比例，例如 0.05 表示 5%。
/// </summary>
/// <param name="Left">左侧扩展比例，以当前截图宽度为基准。</param>
/// <param name="Top">上侧扩展比例，以当前截图高度为基准。</param>
/// <param name="Right">右侧扩展比例，以当前截图宽度为基准。</param>
/// <param name="Bottom">下侧扩展比例，以当前截图高度为基准。</param>
public readonly record struct SearchExpandRatio(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    /// <summary>
    /// 判断四边比例是否均为有限且非负的数字。
    /// 大于 1 的比例是合法的，最终搜索区域会被裁剪到截图边界。
    /// </summary>
    public bool IsValid => double.IsFinite(Left) && Left >= 0
                           && double.IsFinite(Top) && Top >= 0
                           && double.IsFinite(Right) && Right >= 0
                           && double.IsFinite(Bottom) && Bottom >= 0;
}

/// <summary>
/// 参考搜索的锚定方式。
/// 元素在画布右/下侧时通常使用右/下锚定，元素在左/上侧时通常使用左/上锚定；
/// 居中元素使用中心锚定，按画布中心加偏移进行缩放。
/// Auto 用于模拟游戏 UI 的响应式布局，根据参考包围盒所在区域分别选择水平和垂直锚定。
/// </summary>
public enum SearchAnchorMode
{
    /// <summary>
    /// 按参考包围盒中心的 0.4/0.6 分区模拟游戏 UI 的响应式布局。
    /// </summary>
    Auto,

    /// <summary>参考画布贴合当前截图左上侧。</summary>
    TopLeft,

    /// <summary>参考画布贴合当前截图右上侧。</summary>
    TopRight,

    /// <summary>参考画布贴合当前截图左下侧。</summary>
    BottomLeft,

    /// <summary>参考画布贴合当前截图右下侧。</summary>
    BottomRight,

    /// <summary>参考画布在当前截图中水平、垂直居中。</summary>
    Center
}
