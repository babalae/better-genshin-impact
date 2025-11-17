using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

/// <summary>
/// 形态学操作参数类
/// </summary>
public class MorphologyParam
{
    /// <summary>
    /// 运算核大小
    /// </summary>
    public Size KernelSize { get; set; }

    /// <summary>
    /// 运算方法
    /// </summary>
    public MorphTypes MorphType { get; set; }

    /// <summary>
    /// 运算次数
    /// </summary>
    public int Iterations { get; set; }

    /// <summary>
    /// 形态学操作参数构造函数
    /// </summary>
    /// <param name="kernelSize">运算核大小</param>
    /// <param name="morphType">运算方法</param>
    /// <param name="iterations">运算次数</param>
    public MorphologyParam(Size kernelSize, MorphTypes morphType, int iterations)
    {
        KernelSize = kernelSize;
        MorphType = morphType;
        Iterations = iterations;
    }
}

public class ContoursHelper
{
    /// <summary>
    /// 找到指定颜色的矩形
    /// </summary>
    /// <param name="srcMat">图像</param>
    /// <param name="low">RGB色彩低位</param>
    /// <param name="high">RGB色彩高位</param>
    /// <param name="minWidth">矩形最小宽度</param>
    /// <param name="minHeight">矩形最小高度</param>
    /// <param name="morphologyParam">形态学操作参数（可选）</param>
    /// <returns></returns>
    public static List<Rect> FindSpecifyColorRects(Mat srcMat, Scalar low, Scalar high, int minWidth = -1, int minHeight = -1, MorphologyParam? morphologyParam = null)
    {
        try
        {
            using var src = srcMat.Clone();
            Cv2.CvtColor(src, src, ColorConversionCodes.BGR2RGB);
            Cv2.InRange(src, low, high, src);
            
            if (morphologyParam != null)
            {
                using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, morphologyParam.KernelSize);
                Cv2.MorphologyEx(src, src, morphologyParam.MorphType, kernel, iterations: morphologyParam.Iterations);
            }

            Cv2.FindContours(src, out var contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);
            if (contours.Length > 0)
            {
                var boxes = contours.Select(Cv2.BoundingRect).Where(r =>
                {
                    if (minWidth > 0 && r.Width < minWidth)
                    {
                        return false;
                    }
                    if (minHeight > 0 && r.Height < minHeight)
                    {
                        return false;
                    }
                    return true;
                });
                return boxes.ToList();
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        return [];
    }

    public static List<Rect> FindSpecifyColorRects(Mat srcMat, Scalar color, int minWidth = -1, int minHeight = -1)
    {
        return FindSpecifyColorRects(srcMat, color, color, minWidth, minHeight);
    }
    
    public static List<Rect> FindSpecifyColorRects(Mat srcMat, Scalar color, int minWidth, int minHeight, MorphologyParam morphologyParam)
    {
        return FindSpecifyColorRects(srcMat, color, color, minWidth, minHeight, morphologyParam);
    }
}