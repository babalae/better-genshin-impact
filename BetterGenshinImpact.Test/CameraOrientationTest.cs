using OpenCvSharp;
using SharpCompress;

namespace BetterGenshinImpact.Test;

public class CameraOrientationTest
{
    public static (int[], int[], int[]) Test1()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\视角朝向识别\3.png", ImreadModes.Grayscale);
        Cv2.GaussianBlur(mat, mat, new Size(3, 3), 0);
        // 极坐标展开
        var centerPoint = new Point2f(mat.Width / 2f, mat.Height / 2f);
        var polarMat = new Mat();
        Cv2.WarpPolar(mat, polarMat, new Size(360, 360), centerPoint, 360d, InterpolationFlags.Linear, WarpPolarMode.Linear);
        // Cv2.ImShow("polarMat", polarMat);
        var polarRoiMat = new Mat(polarMat, new Rect(10, 0, 70, polarMat.Height));
        Cv2.Rotate(polarRoiMat, polarRoiMat, RotateFlags.Rotate90Counterclockwise);
        Cv2.ImShow("极坐标转换后", polarRoiMat);
        //Cv2.ImWrite(@"E:\HuiTask\更好的原神\自动秘境\视角朝向识别\2_p.png", polarRoiMat);

        var scharrResult = new Mat();
        Cv2.Scharr(polarRoiMat, scharrResult, MatType.CV_32F, 1, 0);

        var scharrResultDisplay = new Mat();
        Cv2.ConvertScaleAbs(scharrResult, scharrResultDisplay);
        Cv2.ImShow("Scharr", scharrResultDisplay);

        // 求波峰
        var left = new int[360];
        var right = new int[360];

        scharrResult.GetArray<float>(out var array);
        var leftPeaks = FindPeaks(array);
        leftPeaks.ForEach(i => left[i % 360]++);

        var reversedArray = array.Select(x => -x).ToArray();
        var rightPeaks = FindPeaks(reversedArray);
        rightPeaks.ForEach(i => right[i % 360]++);

        // 优化
        var left2 = left.Zip(right, (x, y) => Math.Max(x - y, 0)).ToArray();
        var right2 = right.Zip(left, (x, y) => Math.Max(x - y, 0)).ToArray();

        // 左移后相乘
        var rightAfterShift = Shift(right2, -90);
        //var all = left2.Zip(rightAfterShift, (x, y) => x * y).ToArray();

        // for (var i = 0; i < all.Length; i++)
        // {
        //     if (i < 270)
        //     {
        //         all[i] = left[i] * right[i + 90];
        //     }
        //     else
        //     {
        //         all[i] = left[i] * right[i - 270];
        //     }
        // }

        // 左移后相乘 在附近2°内寻找最大值
        var sum = new int[360];
        for (var i = -2; i <= 2; i++)
        {
            var all = left2.Zip(Shift(right2, -90 + i), (x, y) => x * y * (3 - Math.Abs(i)) / 3).ToArray();
            sum = sum.Zip(all, (x, y) => x + y).ToArray();
        }

        var result = new int[360];
        for (var i = -2; i <= 2; i++)
        {
            var all = Shift(sum, i);
            for (var j = 0; j < all.Length; j++)
            {
                all[j] = all[j] * (3 - Math.Abs(i)) / 3;
            }

            result = result.Zip(all, (x, y) => x + y).ToArray();
        }

        return (left2, rightAfterShift, result);
    }

    //static List<int> FindPeaks(double[] data)
    //{
    //    List<int> peakIndices = new List<int>();

    //    for (int i = 1; i < data.Length - 1; i++)
    //    {
    //        if (data[i] > data[i - 1] && data[i] > data[i + 1])
    //        {
    //            peakIndices.Add(i);
    //        }
    //    }

    //    return peakIndices;
    //}

    static List<int> FindPeaks(float[] data)
    {
        List<int> peakIndices = new List<int>();

        for (int i = 1; i < data.Length - 1; i++)
        {
            if (data[i] > data[i - 1] && data[i] > data[i + 1])
            {
                peakIndices.Add(i);
            }
        }

        return peakIndices;
    }

    public static int[] RightShift(int[] array, int k)
    {
        return array.Skip(array.Length - k)
            .Concat(array.Take(array.Length - k))
            .ToArray();
    }

    public static int[] LeftShift(int[] array, int k)
    {
        return array.Skip(k)
            .Concat(array.Take(k))
            .ToArray();
    }

    public static int[] Shift(int[] array, int k)
    {
        if (k > 0)
        {
            return RightShift(array, k);
        }
        else
        {
            return LeftShift(array, -k);
        }
    }

        

    public static void Test2()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\视角朝向识别\1.png", ImreadModes.Color);

        // 极坐标展开
        var centerPoint = new Point2f(mat.Width / 2f, mat.Height / 2f);
        var polarMat = new Mat();
        Cv2.WarpPolar(mat, polarMat, new Size(360, 360), centerPoint, 360d, InterpolationFlags.Linear, WarpPolarMode.Linear);
        var polarRoiMat = new Mat(polarMat, new Rect(20, 0, 70, polarMat.Height));
        Cv2.Rotate(polarRoiMat, polarRoiMat, RotateFlags.Rotate90Counterclockwise);
        Cv2.CvtColor(polarRoiMat, polarRoiMat, ColorConversionCodes.BGR2Lab);
        Cv2.ImShow("极坐标转换后", polarRoiMat);

        var splitMat = polarRoiMat.Split();

        for (int i = 0; i < splitMat.Length; i++)
        {
            Cv2.ImShow($"splitMat{i}", splitMat[i]);
        }

        var andMat = new Mat();
        Cv2.Scharr(splitMat[0], andMat, MatType.CV_8UC1, 1, 0);
        Cv2.ImShow("Scharr", andMat);
    }
}