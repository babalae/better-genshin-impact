using System;
using System.Collections.Generic;
using OpenCvSharp;
using System.Diagnostics;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

public class OpenCvCommonHelper
{
    /// <summary>
    ///     计算灰度图中某个颜色的像素个数
    /// </summary>
    /// <param name="mat"></param>
    /// <param name="color"></param>
    /// <returns></returns>
    public static int CountGrayMatColor(Mat mat, byte color)
    {
        Debug.Assert(mat.Depth() == MatType.CV_8U);
        return SplitChannal(mat, m => CountGrayMatColorC1(m, color)).Sum();
    }

    private static IEnumerable<T> SplitChannal<T>(Mat mat, Func<Mat, T> func)
    {
        if (mat.Empty()) return [];
        if (mat.Channels() == 1)
        {
            return [func.Invoke(mat)];
        }

        Mat[]? channels = null;
        try
        {
            channels = mat.Split();
            return channels.AsParallel().Select(func);
        }
        finally
        {
            // 释放所有分离出的通道内存
            if (channels is not null)
                foreach (var ch in channels)
                    ch.Dispose();
        }
    }

    /// <summary>
    /// 仅限单通道,统计等于color的颜色
    /// </summary>
    /// <param name="mat">矩阵</param>
    /// <param name="color">值</param>
    /// <returns></returns>
    public static int CountGrayMatColorC1(Mat mat, byte color)
    {
        Debug.Assert(mat.Depth() == MatType.CV_8U);
        Debug.Assert(mat.Channels() == 1);
        using var dst = new Mat();
        Cv2.Compare(mat, color, dst, CmpType.EQ);
        return Cv2.CountNonZero(dst);
    }

    /// <summary>
    /// 仅限单通道,统计颜色在lowColor和highColor中范围
    /// </summary>
    /// <param name="mat">矩阵</param>
    /// <param name="lowColor">低</param>
    /// <param name="highColor">高</param>
    /// <returns></returns>
    public static int CountGrayMatColorC1(Mat mat, byte lowColor, byte highColor)
    {
        using var mask = new Mat();
        // 使用InRange直接生成二值掩膜
        Cv2.InRange(mat, new Scalar(lowColor), new Scalar(highColor), mask);
        return Cv2.CountNonZero(mask);
    }

    public static int CountGrayMatColor(Mat mat, byte lowColor, byte highColor)
    {
        return SplitChannal(mat, m => CountGrayMatColorC1(m, lowColor, highColor)).Sum();
    }

    public static Mat Threshold(Mat src, Scalar low, Scalar high)
    {
        using var mask = new Mat();
        using var rgbMat = new Mat();

        Cv2.CvtColor(src, rgbMat, ColorConversionCodes.BGR2RGB);
        Cv2.InRange(rgbMat, low, high, mask);
        // Cv2.Threshold(mask, mask, 0, 255, ThresholdTypes.Binary); //二值化 //不需要
        return mask.Clone();
    }

    public static Mat InRangeHsv(Mat src, Scalar low, Scalar high)
    {
        using var rgbMat = src.CvtColor(ColorConversionCodes.BGR2HSV);
        return rgbMat.InRange(low, high);
    }

    public static Mat InRangeHsvFull(Mat src, Scalar low, Scalar high)
    {
        using var rgbMat = src.CvtColor(ColorConversionCodes.BGR2HSV_FULL);
        return rgbMat.InRange(low, high);
    }

    public static Scalar CommonHSV2OpenCVHSVFull(Scalar commonHSV)
    {
        return new Scalar(commonHSV.Val0 / 255 * 180, commonHSV.Val1 * 255, commonHSV.Val2 * 255);
    }

    public static Mat Threshold(Mat src, Scalar s)
    {
        return Threshold(src, s, s);
    }

    /// <summary>
    ///     和二值化的颜色刚好相反
    /// </summary>
    /// <param name="src"></param>
    /// <param name="s"></param>
    /// <returns></returns>
    public static Mat CreateMask(Mat src, Scalar s)
    {
        var mask = new Mat();
        Cv2.InRange(src, s, s, mask);
        return ~ mask;
    }
    
    /// <summary>
    /// 检测多个点是否都在指定颜色范围内
    /// </summary>
    /// <param name="image">输入图像</param>
    /// <param name="points">要检测的点集合</param>
    /// <param name="lower">颜色下限</param>
    /// <param name="upper">颜色上限</param>
    /// <returns>所有点都在范围内返回 true，否则 false</returns>
    public static bool CheckPointsInRange(Mat image, Point[] points, Scalar lower, Scalar upper)
    {
        if (image.Empty()) return false;
        if (points.Length == 0) return true;
        
        var channels = image.Channels();
        var isContinuous = image.IsContinuous();
        var step = image.Step();
        var width = image.Width;
        var height = image.Height;
        unsafe
        {
            var dataPtr = (byte*)image.Data;
            foreach (var (x, y) in points)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    Debug.WriteLine($"坐标({x}, {y}) 超过图片范围 ({width}, {height})");
                    continue;
                }
                var index = isContinuous ? (y * width + x) * channels : y * step + x * channels;
                for (var ch = 0; ch < channels; ch++)
                {
                    byte pixelValue = dataPtr[index + ch];
                    if (pixelValue < lower[ch] || pixelValue > upper[ch])
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }
}
