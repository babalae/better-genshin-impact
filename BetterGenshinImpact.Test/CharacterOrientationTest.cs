using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Test;

public class CharacterOrientationTest
{
    public static void TestArrow()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\箭头识别\3.png", ImreadModes.Color);
        var lowScalar = new Scalar(0, 207, 255);
        var highScalar = new Scalar(4, 255, 255);
        var gray = OpenCvCommonHelper.Threshold(mat, lowScalar, highScalar);
        Cv2.ImShow("gray", gray);

    }


    static double Distance(OpenCvSharp.Point pt1, OpenCvSharp.Point pt2)
    {
        int deltaX = Math.Abs(pt2.X - pt1.X);
        int deltaY = Math.Abs(pt2.Y - pt1.Y);

        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    public static void Triangle(Mat gray)
    {
        Cv2.FindContours(gray, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        Mat dst = Mat.Zeros(gray.Size(), MatType.CV_8UC3);
        for (int i = 0; i < contours.Length; i++)
        {
            Cv2.DrawContours(dst, contours, i, new Scalar(0, 255, 0), 1, LineTypes.Link4, hierarchy);
        }
        // Cv2.ImShow("目标", dst);

        Mat dst2 = Mat.Zeros(gray.Size(), MatType.CV_8UC3);
        // 遍历轮廓
        for (int i = 0; i < contours.Length; i++)
        {
            // 计算轮廓的周长
            double perimeter = Cv2.ArcLength(contours[i], true);

            // 近似多边形拟合
            OpenCvSharp.Point[] approx = Cv2.ApproxPolyDP(contours[i], 0.04 * perimeter, true);

            // 如果拟合的多边形有三个顶点，认为是三角形
            if (approx.Length == 3)
            {
                // 在图像上绘制三角形的轮廓
                Cv2.DrawContours(dst2, new OpenCvSharp.Point[][] { approx }, -1, Scalar.Green, 1);
                // 计算三条边的长度
                var sideLengths = new double[3];
                sideLengths[0] = Distance(approx[1], approx[2]);
                sideLengths[1] = Distance(approx[2], approx[0]);
                sideLengths[2] = Distance(approx[0], approx[1]);

                var result = sideLengths
                    .Select((value, index) => new { Value = value, Index = index })
                    .OrderBy(item => item.Value)
                    .First();

                // 计算最短线的中点
                var residue = approx.ToList();
                residue.RemoveAt(result.Index);
                var midPoint = new OpenCvSharp.Point((residue[0].X + residue[1].X) / 2, (residue[0].Y + residue[1].Y) / 2);

                // 在图像上绘制直线
                Cv2.Line(dst2, midPoint, approx[result.Index] + (approx[result.Index]- midPoint)*3, Scalar.Red, 1);
            }
        }

        Cv2.ImShow("目标2", dst2);
    }


    public static void TestArrow2()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\箭头识别\1.png", ImreadModes.Color);
        Cv2.GaussianBlur(mat, mat, new Size(3, 3), 0);
        var splitMat = mat.Split();

        //for (int i = 0; i < splitMat.Length; i++)
        //{
        //    Cv2.ImShow($"splitMat{i}", splitMat[i]);
        //}

        // 红蓝通道按位与
        var red = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[0], new Scalar(205), new Scalar(255), red);
        //Cv2.ImShow("red", red);
        var blue = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[2], new Scalar(0), new Scalar(50), blue);
        //Cv2.ImShow("blue", blue);
        var andMat = red & blue;
        Cv2.ImShow("andMat", andMat);

        Triangle(andMat);
    }

    public static void TestArrow3()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\箭头识别\3.png", ImreadModes.Color);
        Cv2.GaussianBlur(mat, mat, new Size(3, 3), 0);
        var splitMat = mat.Split();

        //for (int i = 0; i < splitMat.Length; i++)
        //{
        //    Cv2.ImShow($"splitMat{i}", splitMat[i]);
        //}

        // 红蓝通道按位与
        var red = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[0], new Scalar(255), new Scalar(255), red);
        //Cv2.ImShow("red", red);
        var blue = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[2], new Scalar(0), new Scalar(0), blue);
        //Cv2.ImShow("blue", blue);
        var andMat = red & blue;
        Cv2.ImShow("andMat", andMat);

        //腐蚀
        var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        var res = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.Erode(andMat, res, kernel);
        Cv2.ImShow("erode", res);

        // 膨胀
        // var res2 = new Mat(mat.Size(), MatType.CV_8UC1);
        // Cv2.Dilate(res, res2, kernel);
        // Cv2.ImShow("dilate", res2);

        // 矩形检测
        Cv2.FindContours(res, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        Mat dst = Mat.Zeros(res.Size(), MatType.CV_8UC3);
        for (int i = 0; i < contours.Length; i++)
        {
            // 最小外接矩形
            var rect = Cv2.MinAreaRect(contours[i]);
            // 矩形的四个顶点
            var points = Cv2.BoxPoints(rect);
            // 绘制矩形
            for (int j = 0; j < 4; ++j)
            {
                Cv2.Line(mat, (Point)points[j], (Point)points[(j + 1) % 4], Scalar.Red, 1);
            }
        }
        Cv2.ImShow("目标", mat);

        // Triangle(andMat);
    }
}