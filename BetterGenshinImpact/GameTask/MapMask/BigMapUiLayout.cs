using System;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>识别地图右侧详情或标记面板，保留整屏坐标系中的可见地图范围。</summary>
internal static class BigMapUiLayout
{
    /// <summary>根据跨越地图主体高度的浅色面板边界判断遮挡，不依赖标题或点位类型。</summary>
    internal static Rect GetMapArea(Mat image)
    {
        var full = new Rect(0, 0, image.Width, image.Height);
        if (image.Empty())
        {
            return full;
        }

        // 在统一尺度检测竖边；大片浅色地形通常不会在同一列形成贯穿半屏的明暗跳变。
        var scale = Math.Min(1, 960d / image.Width);
        using var resized = new Mat();
        using var gray = new Mat();
        Cv2.Resize(image, resized, new Size(Math.Max(1, (int)Math.Round(image.Width * scale)),
            Math.Max(1, (int)Math.Round(image.Height * scale))), 0, 0, InterpolationFlags.Area);
        if (resized.Channels() == 1)
        {
            resized.CopyTo(gray);
        }
        else
        {
            Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);
        }

        var bestSupport = 0d;
        var boundary = gray.Width;
        var offset = Math.Max(1, (int)Math.Round(6 * gray.Width / 960d));
        for (var x = Math.Max(offset, gray.Width * 3 / 5); x < gray.Width * 17 / 20; x++)
        {
            var count = 0;
            var supported = 0;
            for (var y = gray.Height * 35 / 100; y < gray.Height * 85 / 100; y += 3)
            {
                var right = gray.At<byte>(y, x);
                if (right > 170 && right - gray.At<byte>(y, x - offset) > 40)
                {
                    supported++;
                }
                count++;
            }

            var support = count == 0 ? 0 : supported / (double)count;
            if (support > bestSupport)
            {
                bestSupport = support;
                boundary = x;
            }
        }

        return bestSupport >= 0.85
            ? new Rect(0, 0, Math.Min(image.Width, (int)Math.Round(boundary / scale)), image.Height)
            : full;
    }
}
