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
        TaskControl.Logger.LogWarning(
            "RecognitionObject {Name} 配置了 ReferenceImageSize/ReferenceBoundingBox，但当前 ImageRegion 不是 GameCaptureRegion 或 DeriveTo1080P 直接派生区域，禁止自动适配匹配。请重新新建一个 RecognitionObject 用于当前区域的识别。",
            ro.Name);
    }

    private static void LogReferenceSearchInvalid(RecognitionObject ro)
    {
        TaskControl.Logger.LogWarning(
            "RecognitionObject {Name} 的 ReferenceImageSize/ReferenceBoundingBox/SearchOptions 配置不完整，禁止自动适配匹配。",
            ro.Name);
    }

    /// <summary>
    /// 获取本次识别最终使用的 ROI 和参考缩放比例。
    /// 返回 false 表示当前参考搜索配置非法或当前区域不允许使用参考坐标，调用方应直接按未命中处理。
    /// </summary>
    internal static bool TryGetReferenceSearchRegion(
        ImageRegion imageRegion,
        RecognitionObject ro,
        out Rect effectiveRegionOfInterest,
        out double scale)
    {
        effectiveRegionOfInterest = default;
        scale = 1d;

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
        if (referenceImageSize.Width <= 0 || referenceImageSize.Height <= 0
            || referenceBoundingBox.Width <= 0 || referenceBoundingBox.Height <= 0)
        {
            LogReferenceSearchInvalid(ro);
            return false;
        }

        // 取较小缩放比，保持参考画布宽高比不变；多出来的边按锚点计算偏移。
        scale = Math.Min(
            imageRegion.SrcMat.Width / (double)referenceImageSize.Width,
            imageRegion.SrcMat.Height / (double)referenceImageSize.Height);
        if (scale <= 0)
        {
            LogReferenceSearchInvalid(ro);
            return false;
        }

        var options = ro.SearchOptions ?? new SearchOptions();
        var expandSize = options.ExpandSize ?? new Size(DefaultSearchExpandSize, DefaultSearchExpandSize);
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

        var predictedX = (int)Math.Round(offsetX + referenceBoundingBox.X * scale);
        var predictedY = (int)Math.Round(offsetY + referenceBoundingBox.Y * scale);
        var predictedWidth = Math.Max(1, (int)Math.Round(referenceBoundingBox.Width * scale));
        var predictedHeight = Math.Max(1, (int)Math.Round(referenceBoundingBox.Height * scale));

        // 预测框外扩后裁剪到当前截图范围，保证后续 Mat ROI 不会越界。
        effectiveRegionOfInterest = new Rect(
                predictedX - expandSize.Width,
                predictedY - expandSize.Height,
                predictedWidth + expandSize.Width * 2,
                predictedHeight + expandSize.Height * 2)
            .ClampTo(imageRegion.SrcMat);

        return effectiveRegionOfInterest.Width > 0 && effectiveRegionOfInterest.Height > 0;
    }

    /// <summary>
    /// 根据参考框缩放模板图。参考搜索下模板大小应跟随 ReferenceBoundingBox 缩放，普通搜索保持原图。
    /// </summary>
    internal static Mat GetEffectiveTemplate(
        RecognitionObject ro,
        Mat template,
        double scale,
        out bool shouldDispose)
    {
        shouldDispose = false;
        if (!HasReferenceSearch(ro))
        {
            return template;
        }

        var referenceBoundingBox = ro.ReferenceBoundingBox!.Value;
        var targetWidth = Math.Max(1, (int)Math.Round(referenceBoundingBox.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(referenceBoundingBox.Height * scale));
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
    /// Auto 模式按参考框中心所在的三分区域判断锚点，靠边元素按边缘锚定，中间元素按居中锚定。
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
