using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Common.Map;

public class CameraOrientation
{
    /// <summary>
    /// 计算当前小地图摄像机朝向的角度
    /// </summary>
    /// <param name="greyMat">完整游戏截图</param>
    /// <returns>角度</returns>
    public static int Compute(Mat greyMat)
    {
        var mat = new Mat(greyMat, new Rect(62, 19, 212, 212));
        Cv2.GaussianBlur(mat, mat, new Size(3, 3), 0);
        // 极坐标展开
        var centerPoint = new Point2f(mat.Width / 2f, mat.Height / 2f);
        var polarMat = new Mat();
        Cv2.WarpPolar(mat, polarMat, new Size(360, 360), centerPoint, 360d, InterpolationFlags.Linear, WarpPolarMode.Linear);
        // Cv2.ImShow("polarMat", polarMat);
        var polarRoiMat = new Mat(polarMat, new Rect(10, 0, 70, polarMat.Height));
        Cv2.Rotate(polarRoiMat, polarRoiMat, RotateFlags.Rotate90Counterclockwise);

        var scharrResult = new Mat();
        Cv2.Scharr(polarRoiMat, scharrResult, MatType.CV_32F, 1, 0);

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

        // 左移后相乘 在附近2°内寻找最大值
        var sum = new int[360];
        for (var i = -2; i <= 2; i++)
        {
            var all = left2.Zip(Shift(right2, -90 + i), (x, y) => x * y * (3 - Math.Abs(i)) / 3).ToArray();
            sum = sum.Zip(all, (x, y) => x + y).ToArray();
        }

        // 卷积
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

        // 计算结果角度
        var maxIndex = result.ToList().IndexOf(result.Max());
        var angle = maxIndex + 45;
        if (angle > 360)
        {
            angle -= 360;
        }

        return angle;
    }

    public static void DrawDirection(ImageRegion region, int angle)
    {
        // 绘图
        var scale = TaskContext.Instance().SystemInfo.AssetScale;
        const int r = 100;
        var center = new Point(168 * scale, 125 * scale); // 地图中心点 后续建议调整
        var x1 = center.X + r * Math.Cos(angle * Math.PI / 180);
        var y1 = center.Y + r * Math.Sin(angle * Math.PI / 180);

        // var line = new LineDrawable(center, new Point(x1, y1))
        // {
        //     Pen = new Pen(Color.Yellow, 1)
        // };
        // VisionContext.Instance().DrawContent.PutLine("camera", line);

        region.DrawLine(center.X, center.Y, (int)x1, (int)y1, "camera", new Pen(Color.Yellow, 1));
    }

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
}
