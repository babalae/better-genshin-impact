using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area.Converter;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.Model.Area;

/// <summary>
/// 负责把 RecognitionObject 中的参考画布坐标转换为当前 ImageRegion 可用的搜索区域。
/// 这部分逻辑依赖“当前区域仍代表整张游戏截图”，因此从 ImageRegion 主流程中拆出来集中约束。
/// </summary>
internal static class ImageRegionReferenceSearchHelper
{
    private const int DefaultSearchExpandSize = 10;

    private enum HorizontalSearchAnchor
    {
        Left,
        Center,
        Right
    }

    private enum VerticalSearchAnchor
    {
        Top,
        Center,
        Bottom
    }

    /// <summary>
    /// 判断识别对象是否启用了参考画布搜索。
    /// 显式 RegionOfInterest 优先级最高，存在时不再使用 ReferenceImageSize/ReferenceBoundingBox 推导 ROI。
    /// </summary>
    internal static bool HasReferenceSearch(RecognitionObject ro)
    {
        return ro.RegionOfInterest == default
               && ro.ReferenceImageSize.HasValue
               && ro.ReferenceBoundingBox.HasValue;
    }

    /// <summary>
    /// 只配置了部分参考搜索参数时不允许继续识别，避免 SearchOptions 被误以为可以单独生效。
    /// </summary>
    private static bool HasPartialReferenceSearch(RecognitionObject ro)
    {
        return ro.RegionOfInterest == default
               && (ro.ReferenceImageSize.HasValue || ro.ReferenceBoundingBox.HasValue || ro.SearchOptions != null)
               && !HasReferenceSearch(ro);
    }

    /// <summary>
    /// 参考坐标只能用于整张游戏截图，或者 GameCaptureRegion.DeriveTo1080P() 直接得到的第一层缩放图。
    /// 再经过 DeriveCrop 等局部裁剪后，参考画布坐标已经失去全局语义，必须拒绝使用。
    /// </summary>
    private static bool CanUseReferenceSearch(ImageRegion imageRegion)
    {
        return imageRegion is GameCaptureRegion
               || (imageRegion.Prev is GameCaptureRegion && imageRegion.PrevConverter is ScaleConverter);
    }

    private static void LogReferenceSearchNotAllowed(RecognitionObject ro)
    {
        // 测试和离线识别可能没有 WPF App；此时不为一条诊断日志初始化完整应用宿主。
        if (System.Windows.Application.Current is App)
        {
            TaskControl.Logger.LogWarning(
                "RecognitionObject {Name} 配置了 ReferenceImageSize/ReferenceBoundingBox，但当前 ImageRegion 不是 GameCaptureRegion 或 DeriveTo1080P 直接派生区域，禁止自动适配匹配。请重新新建一个 RecognitionObject 用于当前区域的识别。",
                ro.Name);
        }
    }

    private static void LogReferenceSearchInvalid(RecognitionObject ro)
    {
        if (System.Windows.Application.Current is App)
        {
            TaskControl.Logger.LogWarning(
                "RecognitionObject {Name} 的 ReferenceImageSize/ReferenceBoundingBox/SearchOptions 配置不完整，禁止自动适配匹配。",
                ro.Name);
        }
    }

    /// <summary>
    /// 获取本次识别最终使用的 ROI 和模板在当前截图中的缩放尺寸。
    /// 返回 false 表示当前参考搜索配置非法或当前区域不允许使用参考坐标，调用方应直接按未命中处理。
    /// </summary>
    internal static bool TryGetReferenceSearchRegion(
        ImageRegion imageRegion,
        RecognitionObject ro,
        out Rect effectiveRegionOfInterest,
        out Size effectiveReferenceBoundingBoxSize)
    {
        effectiveRegionOfInterest = default;
        effectiveReferenceBoundingBoxSize = default;

        if (HasPartialReferenceSearch(ro))
        {
            LogReferenceSearchInvalid(ro);
            return false;
        }

        if (!HasReferenceSearch(ro))
        {
            effectiveRegionOfInterest = ro.RegionOfInterest;
            return true;
        }

        if (!CanUseReferenceSearch(imageRegion))
        {
            LogReferenceSearchNotAllowed(ro);
            return false;
        }

        var referenceImageSize = ro.ReferenceImageSize!.Value;
        var referenceBoundingBox = ro.ReferenceBoundingBox!.Value;
        var options = ro.SearchOptions ?? new SearchOptions();
        if (referenceImageSize.Width <= 0 || referenceImageSize.Height <= 0
            || referenceBoundingBox.Width <= 0 || referenceBoundingBox.Height <= 0
            || options.ReferenceSearchBox is { Width: <= 0 } or { Height: <= 0 }
            || options.ExpandPercent is { IsValid: false })
        {
            LogReferenceSearchInvalid(ro);
            return false;
        }

        // 取较小缩放比，保持参考画布宽高比不变；多出来的边按锚点计算偏移。
        var scale = Math.Min(
            imageRegion.SrcMat.Width / (double)referenceImageSize.Width,
            imageRegion.SrcMat.Height / (double)referenceImageSize.Height);
        if (scale <= 0)
        {
            LogReferenceSearchInvalid(ro);
            return false;
        }

        var (horizontalAnchor, verticalAnchor) = ResolveSearchAnchor(options.AnchorMode, referenceBoundingBox, referenceImageSize);

        var scaledReferenceWidth = referenceImageSize.Width * scale;
        var scaledReferenceHeight = referenceImageSize.Height * scale;

        // 输入图和参考图宽高比不一致时，锚点决定参考画布贴向哪一侧。
        var offsetX = horizontalAnchor switch
        {
            HorizontalSearchAnchor.Right => imageRegion.SrcMat.Width - scaledReferenceWidth,
            HorizontalSearchAnchor.Center => (imageRegion.SrcMat.Width - scaledReferenceWidth) / 2d,
            _ => 0d
        };

        var offsetY = verticalAnchor switch
        {
            VerticalSearchAnchor.Bottom => imageRegion.SrcMat.Height - scaledReferenceHeight,
            VerticalSearchAnchor.Center => (imageRegion.SrcMat.Height - scaledReferenceHeight) / 2d,
            _ => 0d
        };

        // 模板框和独立搜索框共用同一个参考矩形转换；模板尺寸继续按原有的“宽高直接缩放后取整”
        // 规则计算，保证未使用新字段时的模板缩放结果完全兼容。
        var transformedReferenceBoundingBox = TransformReferenceRect(referenceBoundingBox, scale, offsetX, offsetY);
        effectiveReferenceBoundingBoxSize = new Size(
            Math.Max(1, (int)Math.Round(referenceBoundingBox.Width * scale)),
            Math.Max(1, (int)Math.Round(referenceBoundingBox.Height * scale)));
        // 独立搜索框与模板位置共用参考画布的缩放和锚定偏移，避免宽高比变化时发生相对位置漂移。
        var baseSearchRegion = options.ReferenceSearchBox is { } referenceSearchBox
            ? TransformReferenceRect(referenceSearchBox, scale, offsetX, offsetY)
            : transformedReferenceBoundingBox;

        // 百分比扩展按当前截图宽高计算，并优先于像素扩展；最终统一裁剪到截图边界。
        effectiveRegionOfInterest = ExpandAndClampSearchRegion(baseSearchRegion, imageRegion.SrcMat.Size(), options);
        if (effectiveRegionOfInterest.Width <= 0 || effectiveRegionOfInterest.Height <= 0)
        {
            return false;
        }

        // OpenCV 要求搜索区域不能小于模板；提前返回未命中，避免创建非法 Mat 或触发匹配异常。
        return ro.RecognitionType != RecognitionTypes.TemplateMatch
               || (effectiveRegionOfInterest.Width >= effectiveReferenceBoundingBoxSize.Width
                   && effectiveRegionOfInterest.Height >= effectiveReferenceBoundingBoxSize.Height);
    }

    /// <summary>
    /// 根据参考框缩放模板图。参考搜索下模板大小应跟随 ReferenceBoundingBox 缩放，普通搜索保持原图。
    /// </summary>
    internal static Mat GetEffectiveTemplate(
        RecognitionObject ro,
        Mat template,
        Size effectiveReferenceBoundingBoxSize,
        out bool shouldDispose)
    {
        shouldDispose = false;
        if (!HasReferenceSearch(ro))
        {
            return template;
        }

        var targetWidth = Math.Max(1, effectiveReferenceBoundingBoxSize.Width);
        var targetHeight = Math.Max(1, effectiveReferenceBoundingBoxSize.Height);
        if (template.Width == targetWidth && template.Height == targetHeight)
        {
            return template;
        }

        var resized = new Mat();
        Cv2.Resize(template, resized, new Size(targetWidth, targetHeight));
        shouldDispose = true;
        return resized;
    }

    /// <summary>
    /// 将参考画布中的矩形转换到当前截图坐标系。
    /// 分别转换左右、上下边界后再计算宽高，避免位置和尺寸独立舍入导致一像素偏差。
    /// </summary>
    private static Rect TransformReferenceRect(Rect referenceRect, double scale, double offsetX, double offsetY)
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

    /// <summary>
    /// 按搜索选项扩展基础搜索框并裁剪到当前截图范围。
    /// ExpandPercent 存在时，左右以截图宽度、上下以截图高度为基准，且完全忽略像素 ExpandSize。
    /// </summary>
    private static Rect ExpandAndClampSearchRegion(Rect baseRegion, Size imageSize, SearchOptions options)
    {
        double expandLeft;
        double expandTop;
        double expandRight;
        double expandBottom;

        if (options.ExpandPercent is { } ratio)
        {
            expandLeft = imageSize.Width * ratio.Left;
            expandTop = imageSize.Height * ratio.Top;
            expandRight = imageSize.Width * ratio.Right;
            expandBottom = imageSize.Height * ratio.Bottom;
        }
        else
        {
            var expandSize = options.ExpandSize ?? new Size(DefaultSearchExpandSize, DefaultSearchExpandSize);
            // 像素模式保留历史行为；配置校验由 JSON 编辑器等上层入口负责。
            expandLeft = expandRight = expandSize.Width;
            expandTop = expandBottom = expandSize.Height;
        }

        // 先以 double 计算并限制到截图范围，再转为 int，可安全处理大于 100% 的扩展比例。
        var left = (int)Math.Round(Math.Clamp(baseRegion.Left - expandLeft, 0d, imageSize.Width));
        var top = (int)Math.Round(Math.Clamp(baseRegion.Top - expandTop, 0d, imageSize.Height));
        var right = (int)Math.Round(Math.Clamp(baseRegion.Right + expandRight, 0d, imageSize.Width));
        var bottom = (int)Math.Round(Math.Clamp(baseRegion.Bottom + expandBottom, 0d, imageSize.Height));

        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// 模板被缩放时，遮罩也需要同步缩放；遮罩是离散标记，使用最近邻避免产生中间灰度。
    /// </summary>
    internal static Mat? GetEffectiveMask(
        Mat? maskMat,
        Mat effectiveTemplate,
        out bool shouldDispose)
    {
        shouldDispose = false;
        if (maskMat == null || (maskMat.Width == effectiveTemplate.Width && maskMat.Height == effectiveTemplate.Height))
        {
            return maskMat;
        }

        var resized = new Mat();
        Cv2.Resize(maskMat, resized, new Size(effectiveTemplate.Width, effectiveTemplate.Height), 0, 0, InterpolationFlags.Nearest);
        shouldDispose = true;
        return resized;
    }

    private static (HorizontalSearchAnchor horizontal, VerticalSearchAnchor vertical) ResolveSearchAnchor(
        SearchAnchorMode anchorMode,
        Rect referenceBoundingBox,
        Size referenceImageSize)
    {
        return anchorMode switch
        {
            SearchAnchorMode.TopLeft => (HorizontalSearchAnchor.Left, VerticalSearchAnchor.Top),
            SearchAnchorMode.TopRight => (HorizontalSearchAnchor.Right, VerticalSearchAnchor.Top),
            SearchAnchorMode.BottomLeft => (HorizontalSearchAnchor.Left, VerticalSearchAnchor.Bottom),
            SearchAnchorMode.BottomRight => (HorizontalSearchAnchor.Right, VerticalSearchAnchor.Bottom),
            SearchAnchorMode.Center => (HorizontalSearchAnchor.Center, VerticalSearchAnchor.Center),
            _ => ResolveAutoSearchAnchor(referenceBoundingBox, referenceImageSize)
        };
    }

    /// <summary>
    /// Auto 模式模拟游戏 UI 的响应式布局：按参考框中心所在区域分别选择水平和垂直锚定，
    /// 靠边元素跟随对应边缘，中部元素跟随画布中心。0.4/0.6 分界用于保留中部响应区域。
    /// </summary>
    private static (HorizontalSearchAnchor horizontal, VerticalSearchAnchor vertical) ResolveAutoSearchAnchor(
        Rect referenceBoundingBox,
        Size referenceImageSize)
    {
        var centerX = referenceBoundingBox.X + referenceBoundingBox.Width / 2d;
        var centerY = referenceBoundingBox.Y + referenceBoundingBox.Height / 2d;

        var horizontal = centerX switch
        {
            var x when x < referenceImageSize.Width * 0.4 => HorizontalSearchAnchor.Left,
            var x when x > referenceImageSize.Width * 0.6 => HorizontalSearchAnchor.Right,
            _ => HorizontalSearchAnchor.Center
        };

        var vertical = centerY switch
        {
            var y when y < referenceImageSize.Height * 0.4 => VerticalSearchAnchor.Top,
            var y when y > referenceImageSize.Height * 0.6 => VerticalSearchAnchor.Bottom,
            _ => VerticalSearchAnchor.Center
        };

        return (horizontal, vertical);
    }
}
