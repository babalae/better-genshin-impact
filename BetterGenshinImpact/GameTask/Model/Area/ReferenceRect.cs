using System;
using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Model.Area;

/// <summary>
/// 把参考画布（默认 1920×1080）中的矩形换算到当前截图坐标：按较小缩放比保持宽高比，
/// 宽高比不一致时由锚点决定参考画布贴向哪一侧。与 Recognition.json 中 search.anchor / search.box 的规则一致，
/// 手动指定 ROI 时可直接使用，兼容 16:10、21:9 等非 16:9 分辨率。
/// </summary>
public static class ReferenceRect
{
    /// <summary>默认参考画布尺寸，与识别资源的标注分辨率一致。</summary>
    public static readonly Size DefaultReferenceSize = new(1920, 1080);

    /// <summary>水平锚定方向。</summary>
    private enum HorizontalAnchor
    {
        Left,
        Center,
        Right
    }

    /// <summary>垂直锚定方向。</summary>
    private enum VerticalAnchor
    {
        Top,
        Center,
        Bottom
    }

    /// <summary>
    /// 把参考画布中的矩形换算到当前截图，并裁剪到截图范围内。
    /// </summary>
    /// <param name="referenceRect">参考画布坐标系中的矩形，例如 1080P 下量出的 ROI。</param>
    /// <param name="imageSize">当前截图尺寸。</param>
    /// <param name="anchorMode">锚定方式；Auto 按矩形中心所在区域自动选择。</param>
    /// <param name="referenceSize">参考画布尺寸；null 时为 1920×1080。</param>
    /// <returns>当前截图中的矩形；完全落在截图外时宽高为 0。</returns>
    public static Rect ToImage(Rect referenceRect, Size imageSize, SearchAnchorMode anchorMode = SearchAnchorMode.Auto,
        Size? referenceSize = null)
    {
        var reference = referenceSize ?? DefaultReferenceSize;
        if (!TryResolve(reference, imageSize, anchorMode, referenceRect, out var scale, out var offsetX, out var offsetY))
        {
            return default;
        }

        return Clamp(Transform(referenceRect, scale, offsetX, offsetY), imageSize);
    }

    /// <summary>
    /// 把 1080P 参考画布中的矩形换算到本区域坐标。本区域应代表整张游戏截图（例如 CaptureToRectArea 的结果），
    /// 局部裁剪后的区域没有全局坐标语义，不应使用。
    /// </summary>
    /// <param name="region">整张游戏截图。</param>
    /// <param name="referenceRect">1080P 下的矩形。</param>
    /// <param name="anchorMode">锚定方式；Auto 按矩形中心所在区域自动选择。</param>
    /// <returns>本区域中的矩形，已裁剪到区域范围内。</returns>
    public static Rect ReferenceRoi(this ImageRegion region, Rect referenceRect, SearchAnchorMode anchorMode = SearchAnchorMode.Auto)
        => ToImage(referenceRect, new Size(region.Width, region.Height), anchorMode);

    /// <summary>
    /// 计算参考画布到当前截图的缩放比与锚定偏移。
    /// </summary>
    /// <param name="referenceSize">参考画布尺寸。</param>
    /// <param name="imageSize">当前截图尺寸。</param>
    /// <param name="anchorMode">锚定方式。</param>
    /// <param name="anchorRect">Auto 模式下用于判断锚点的参考矩形。</param>
    /// <param name="scale">缩放比。</param>
    /// <param name="offsetX">水平偏移。</param>
    /// <param name="offsetY">垂直偏移。</param>
    /// <returns>尺寸无效（缩放比不大于 0）时返回 false。</returns>
    internal static bool TryResolve(Size referenceSize, Size imageSize, SearchAnchorMode anchorMode, Rect anchorRect,
        out double scale, out double offsetX, out double offsetY)
    {
        offsetX = 0;
        offsetY = 0;
        scale = referenceSize.Width <= 0 || referenceSize.Height <= 0
            ? 0
            : Math.Min(imageSize.Width / (double)referenceSize.Width, imageSize.Height / (double)referenceSize.Height);
        if (scale <= 0)
        {
            return false;
        }

        // 取较小缩放比，保持参考画布宽高比不变；多出来的边按锚点计算偏移
        var (horizontal, vertical) = ResolveAnchor(anchorMode, anchorRect, referenceSize);
        var scaledWidth = referenceSize.Width * scale;
        var scaledHeight = referenceSize.Height * scale;

        // 输入图和参考图宽高比不一致时，锚点决定参考画布贴向哪一侧
        offsetX = horizontal switch
        {
            HorizontalAnchor.Right => imageSize.Width - scaledWidth,
            HorizontalAnchor.Center => (imageSize.Width - scaledWidth) / 2d,
            _ => 0d
        };
        offsetY = vertical switch
        {
            VerticalAnchor.Bottom => imageSize.Height - scaledHeight,
            VerticalAnchor.Center => (imageSize.Height - scaledHeight) / 2d,
            _ => 0d
        };
        return true;
    }

    /// <summary>
    /// 将参考画布中的矩形转换到当前截图坐标系。
    /// 分别转换左右、上下边界后再计算宽高，避免位置和尺寸独立舍入导致一像素偏差。
    /// </summary>
    internal static Rect Transform(Rect referenceRect, double scale, double offsetX, double offsetY)
    {
        var left = (int)Math.Round(offsetX + referenceRect.Left * scale);
        var top = (int)Math.Round(offsetY + referenceRect.Top * scale);
        var right = (int)Math.Round(offsetX + referenceRect.Right * scale);
        var bottom = (int)Math.Round(offsetY + referenceRect.Bottom * scale);

        return new Rect(
            left,
            top,
            Math.Max(1, right - left),
            Math.Max(1, bottom - top));
    }

    /// <summary>把矩形裁剪到截图范围内。</summary>
    /// <param name="rect">矩形。</param>
    /// <param name="imageSize">截图尺寸。</param>
    /// <returns>裁剪后的矩形；没有交集时宽高为 0。</returns>
    private static Rect Clamp(Rect rect, Size imageSize)
    {
        var left = Math.Clamp(rect.Left, 0, imageSize.Width);
        var top = Math.Clamp(rect.Top, 0, imageSize.Height);
        var right = Math.Clamp(rect.Right, 0, imageSize.Width);
        var bottom = Math.Clamp(rect.Bottom, 0, imageSize.Height);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>把锚定方式拆分为水平与垂直方向。</summary>
    private static (HorizontalAnchor horizontal, VerticalAnchor vertical) ResolveAnchor(
        SearchAnchorMode anchorMode, Rect anchorRect, Size referenceSize)
    {
        return anchorMode switch
        {
            SearchAnchorMode.TopLeft => (HorizontalAnchor.Left, VerticalAnchor.Top),
            SearchAnchorMode.TopRight => (HorizontalAnchor.Right, VerticalAnchor.Top),
            SearchAnchorMode.BottomLeft => (HorizontalAnchor.Left, VerticalAnchor.Bottom),
            SearchAnchorMode.BottomRight => (HorizontalAnchor.Right, VerticalAnchor.Bottom),
            SearchAnchorMode.Center => (HorizontalAnchor.Center, VerticalAnchor.Center),
            _ => ResolveAutoAnchor(anchorRect, referenceSize)
        };
    }

    /// <summary>
    /// Auto 模式模拟游戏 UI 的响应式布局：按参考框中心所在区域分别选择水平和垂直锚定，
    /// 靠边元素跟随对应边缘，中部元素跟随画布中心。0.4/0.6 分界用于保留中部响应区域。
    /// </summary>
    private static (HorizontalAnchor horizontal, VerticalAnchor vertical) ResolveAutoAnchor(Rect anchorRect, Size referenceSize)
    {
        var centerX = anchorRect.X + anchorRect.Width / 2d;
        var centerY = anchorRect.Y + anchorRect.Height / 2d;

        var horizontal = centerX switch
        {
            var x when x < referenceSize.Width * 0.4 => HorizontalAnchor.Left,
            var x when x > referenceSize.Width * 0.6 => HorizontalAnchor.Right,
            _ => HorizontalAnchor.Center
        };

        var vertical = centerY switch
        {
            var y when y < referenceSize.Height * 0.4 => VerticalAnchor.Top,
            var y when y > referenceSize.Height * 0.6 => VerticalAnchor.Bottom,
            _ => VerticalAnchor.Center
        };

        return (horizontal, vertical);
    }
}
