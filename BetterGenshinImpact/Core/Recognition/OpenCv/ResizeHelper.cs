using OpenCvSharp;
using System;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

public class ResizeHelper
{
    /// <summary>
    ///     等比放大的
    /// </summary>
    /// <param name="src"></param>
    /// <param name="scale"></param>
    /// <returns></returns>
    public static Mat Resize(Mat src, double scale)
    {
        if (Math.Abs(scale - 1) > 0.00001)
        {
            return Resize(src, scale, scale);
        }
        return src;
    }

    public static Mat Resize(Mat src, double widthScale, double heightScale)
    {
        if (Math.Abs(widthScale - 1) > 0.00001 || Math.Abs(heightScale - 1) > 0.00001)
        {
            var dst = new Mat();
            Cv2.Resize(src, dst, new Size(src.Width * widthScale, src.Height * heightScale));
            return dst;
        }

        return src;
    }

    public static Mat ResizeTo(Mat src, int width, int height)
    {
        if (src.Width != width || src.Height != height)
        {
            var dst = new Mat();
            Cv2.Resize(src, dst, new Size(width, height));
            return dst;
        }

        return src;
    }
}
