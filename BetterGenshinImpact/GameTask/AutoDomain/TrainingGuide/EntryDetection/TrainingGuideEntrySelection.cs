using System;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

/// <summary>检查左侧入口标题所在行的浅色选中背景；不使用锁图标判断选中。</summary>
public static class TrainingGuideEntrySelection
{
    public const double MinimumHighlightRatio = .8;

    public static double HighlightRatio(Mat screen, int titleY, int titleHeight)
    {
        // 取列表右侧、与标题同高的空白区域，避开文字、锁和左侧悬浮日志。
        // 必须由当前截图的 OCR 提供位置，不能复用滚动前的坐标。
        var left = (int)(screen.Width * .32);
        var right = (int)(screen.Width * .43);
        if (titleHeight <= 0 || titleY < screen.Height * .12 ||
            titleY + titleHeight > screen.Height * .96 || right <= left)
            return 0;

        using var region = new Mat(screen, new Rect(left, titleY, right - left, titleHeight));
        using var bgr = new Mat();
        if (region.Channels() == 4) Cv2.CvtColor(region, bgr, ColorConversionCodes.BGRA2BGR);
        else if (region.Channels() == 3) region.CopyTo(bgr);
        else return 0;

        var highlighted = 0;
        for (var y = 0; y < bgr.Height; y++)
        for (var x = 0; x < bgr.Width; x++)
        {
            var pixel = bgr.At<Vec3b>(y, x);
            var minimum = Math.Min(pixel.Item0, Math.Min(pixel.Item1, pixel.Item2));
            var maximum = Math.Max(pixel.Item0, Math.Max(pixel.Item1, pixel.Item2));
            // 米白色高亮；普通深蓝底和黄色悬停边框不能满足大面积浅色条件。
            if (minimum >= 170 && maximum - minimum <= 75 && pixel.Item2 + 10 >= pixel.Item0)
                highlighted++;
        }
        return highlighted / (double)(bgr.Width * bgr.Height);
    }
}
