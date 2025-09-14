using System;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoPick;

using OpenCvSharp;

public static class TextRectExtractor
{
    /// <summary>
    /// 从图片中提取文字范围（假定文字从最左边贴边开始，向右连续）
    /// 结果矩形固定 x=0,y=0,h=原图高度，只计算连续文字宽度。
    /// </summary>
    public static Rect GetTextBoundingRect(Mat textMat, out Mat bin)
    {
        // 转换为灰度图
        Mat gray = new Mat();
        if (textMat.Channels() == 3)
        {
            Cv2.CvtColor(textMat, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            gray = textMat.Clone();
        }

        // 使用阈值160进行二值化处理
        bin = new Mat();
        Cv2.Threshold(gray, bin, 160, 255, ThresholdTypes.Binary);

        // 形态学操作：先腐蚀后膨胀，去除噪点并保持文字完整
        Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        Cv2.Erode(bin, bin, kernel, iterations: 1);
        Cv2.Dilate(bin, bin, kernel, iterations: 2);
        kernel.Dispose();
        gray.Dispose();
        return ProjectionRect(textMat, bin);
    }

    private static Rect ProjectionRect(Mat textMat, Mat bin)
    {
        // 投影：对行做 ReduceSum，得到 1 x width 的列和
        using var projection = new Mat();
        Cv2.Reduce(bin, projection, 0, ReduceTypes.Sum, MatType.CV_32S);
        int width = projection.Cols;
        projection.GetArray(out int[] colSums);

        int maxGap = 30; // 允许的最大连续空列数
        int gapCount = 0;
        int lastNonEmpty = -1;

        for (int x = 0; x < width; x++)
        {
            bool hasInk = colSums[x] > 0;
            if (hasInk)
            {
                lastNonEmpty = x;
                gapCount = 0;
            }
            else
            {
                gapCount++;
                if (gapCount > maxGap)
                {
                    break;
                }
            }
        }
        
        if (lastNonEmpty == -1)
        {
            // 没有检测到文字
            return new Rect();
        }

        Rect boundingRect = new Rect(0, 0, lastNonEmpty, textMat.Height);
        return boundingRect;
    }
}
