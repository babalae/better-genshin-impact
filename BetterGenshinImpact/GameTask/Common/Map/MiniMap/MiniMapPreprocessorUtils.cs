using System.Linq;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.MiniMap;

public static class MiniMapPreprocessorUtils
{
    public const int Size = 156;

    public static float[] LinearSpaced(float a, float b, int n, bool endpoint = true)
    {
        return endpoint ? 
            Enumerable.Range(0, n).Select(i => a + (b - a) * i / (n - 1)).ToArray() : 
            Enumerable.Range(0, n).Select(i => a + (b - a) * i / n).ToArray();
    }

    public static void MaxMinChannels(Mat bgrMat, Mat cmax, Mat cmin)
    {
        var bgr = bgrMat.Split();
        using (bgr[0])
        using (bgr[1])
        using (bgr[2])
        {
            Cv2.Max(bgr[2], bgr[1], cmax);
            Cv2.Max(cmax, bgr[0], cmax);
            Cv2.Min(bgr[2], bgr[1], cmin);
            Cv2.Min(cmin, bgr[0], cmin);
        }
    }

    public static void ApplyMask(Mat inputMat, Mat mask, Scalar bkg)
    {
        Cv2.Subtract(inputMat, bkg, inputMat);
        Cv2.Divide(inputMat, mask, inputMat, 255, MatType.CV_32F);
        Cv2.Add(inputMat, bkg, inputMat);
    }

    public static void BgrToHue(Mat bgrMat, Mat hImg, Mat faImg)
    {
        using var hls = new Mat();
        using var h = new Mat();
        using var fa = new Mat();
        Cv2.CvtColor(bgrMat, hls, ColorConversionCodes.BGR2HLS_FULL);
        Cv2.CvtColor(bgrMat, fa, ColorConversionCodes.BGR2GRAY);
        Cv2.ExtractChannel(hls, h, 0);
        h.ConvertTo(hImg, MatType.CV_32FC1);
        fa.ConvertTo(faImg, MatType.CV_32FC1);
    }

    public static void RightShiftCv(Mat input, Mat output, int k)
    {
        var part1 = input[0, input.Rows, input.Cols - k, input.Cols];  // 后k个元素
        var part2 = input[0, input.Rows, 0, input.Cols - k];  // 前面的元素
        Cv2.HConcat(part1, part2, output);
    }


}