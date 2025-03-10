using System.Diagnostics;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;

namespace BetterGenshinImpact.Test.Simple.MiniMap;

public class CharacterOrientationTest
{
    public static void TestArrow()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\箭头识别\3.png", ImreadModes.Color);
        var lowScalar = new Scalar(0, 207, 255);
        var highScalar = new Scalar(0, 208, 255);
        var gray = OpenCvCommonHelper.Threshold(mat, lowScalar, highScalar);
        Cv2.ImShow("gray", gray);
    }

    static double Distance(Point pt1, Point pt2)
    {
        int deltaX = Math.Abs(pt2.X - pt1.X);
        int deltaY = Math.Abs(pt2.Y - pt1.Y);

        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    static double Distance(Point2f pt1, Point2f pt2)
    {
        var deltaX = Math.Abs(pt2.X - pt1.X);
        var deltaY = Math.Abs(pt2.Y - pt1.Y);

        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    // static Point2f Midpoint(Point2f p1, Point2f p2)
    // {
    //     var midX = (p1.X + p2.X) / 2;
    //     var midY = (p1.Y + p2.Y) / 2;
    //     return new Point2f(midX, midY);
    // }

    static Point Midpoint(Point p1, Point p2)
    {
        var midX = (p1.X + p2.X) / 2;
        var midY = (p1.Y + p2.Y) / 2;
        return new Point(midX, midY);
    }

    public static void Triangle(Mat src, Mat gray)
    {
        Cv2.FindContours(gray, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxNone);
        Mat dst = Mat.Zeros(gray.Size(), MatType.CV_8UC3);
        for (int i = 0; i < contours.Length; i++)
        {
            Cv2.DrawContours(src, contours, i, Scalar.Red, 1, LineTypes.Link4, hierarchy);
        }
        // Cv2.ImShow("目标", dst);

        Mat dst2 = Mat.Zeros(gray.Size(), MatType.CV_8UC3);
        // 遍历轮廓
        for (int i = 0; i < contours.Length; i++)
        {
            // 计算轮廓的周长
            double perimeter = Cv2.ArcLength(contours[i], true);

            // 近似多边形拟合
            Point[] approx = Cv2.ApproxPolyDP(contours[i], 0.04 * perimeter, true);

            // 如果拟合的多边形有三个顶点，认为是三角形
            if (approx.Length == 3)
            {
                // 在图像上绘制三角形的轮廓
                Cv2.DrawContours(src, new Point[][] { approx }, -1, Scalar.Green, 1);
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
                var midPoint = new Point((residue[0].X + residue[1].X) / 2, (residue[0].Y + residue[1].Y) / 2);

                // 在图像上绘制直线
                Cv2.Line(src, midPoint, approx[result.Index] + (approx[result.Index] - midPoint) * 3, Scalar.Red, 1);

                Debug.WriteLine(CalculateAngle(midPoint, approx[result.Index]));
            }
        }

        Cv2.ImShow("目标2", src);
    }

    public static void TestArrow2()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\箭头识别\s1.png", ImreadModes.Color);
        Cv2.GaussianBlur(mat, mat, new Size(3, 3), 0);
        var splitMat = mat.Split();

        //for (int i = 0; i < splitMat.Length; i++)
        //{
        //    Cv2.ImShow($"splitMat{i}", splitMat[i]);
        //}

        // 红蓝通道按位与
        var red = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[0], new Scalar(250), new Scalar(255), red);
        //Cv2.ImShow("red", red);
        var blue = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[2], new Scalar(0), new Scalar(10), blue);
        //Cv2.ImShow("blue", blue);
        var andMat = red & blue;
        Cv2.ImShow("andMat2", andMat);
        Triangle(mat, andMat);
    }

    public static void TestArrow3()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\箭头识别\s1.png", ImreadModes.Color);
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
        // Cv2.ImShow("andMat", andMat);

        //腐蚀
        var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
        var res = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.Erode(andMat.ToMat(), res, kernel);
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

            if (Distance(points[0], points[1]) > Distance(points[1], points[2]))
            {
                Debug.WriteLine(CalculateAngle(points[0], points[1]));
            }
            else
            {
                Debug.WriteLine(CalculateAngle(points[1], points[2]));
            }
        }

        Cv2.ImShow("目标", mat);

        TestArrow2();
    }

    static double CalculateAngle(Point2f point1, Point2f point2)
    {
        var angleRadians = Math.Atan2(point2.Y - point1.Y, point2.X - point1.X);
        var angleDegrees = angleRadians * (180 / Math.PI);

        // 将角度调整为正值
        if (angleDegrees < 0)
        {
            angleDegrees += 360;
        }

        return angleDegrees;
    }

    public static void FloodFill()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\箭头识别\s1.png", ImreadModes.Color);
        Cv2.GaussianBlur(mat, mat, new Size(3, 3), 0);
        var splitMat = mat.Split();

        //for (int i = 0; i < splitMat.Length; i++)
        //{
        //    Cv2.ImShow($"splitMat{i}", splitMat[i]);
        //}

        // 1. 红蓝通道按位与
        var red = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[0], new Scalar(250), new Scalar(255), red);
        //Cv2.ImShow("red", red);
        var blue = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[2], new Scalar(0), new Scalar(10), blue);
        //Cv2.ImShow("blue", blue);
        var andMat = new Mat(mat.Size(), MatType.CV_8UC1);

        Cv2.BitwiseAnd(red, blue, andMat);
        Cv2.ImShow("andMat2", andMat);

        // 寻找轮廓
        Cv2.FindContours(andMat, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Mat dst = Mat.Zeros(andMat.Size(), MatType.CV_8UC3);
        for (int i = 0; i < contours.Length; i++)
        {
            Cv2.DrawContours(dst, contours, i, Scalar.Red, 1, LineTypes.Link4, hierarchy);
        }

        Cv2.ImShow("寻找轮廓", dst);

        // 计算最大外接矩形
        if (contours.Length > 0)
        {
            var boxes = contours.Select(Cv2.BoundingRect).Where(w => w.Height >= 2);
            var boxArray = boxes as Rect[] ?? boxes.ToArray();
            if (boxArray.Count() != 1)
            {
                throw new Exception("找到多个外接矩形");
            }

            var box = boxArray.First();

            // 剪裁出准备泛洪的区域（放大4倍的区域）
            var newSrcMat = new Mat(mat, new Rect(box.X - box.Width / 2, box.Y - box.Height / 2, box.Width * 2, box.Height * 2));
            Cv2.ImShow("剪裁出准备泛洪的区域", newSrcMat);

            // 中心点作为种子点
            var seedPoint = new Point(newSrcMat.Width / 2, newSrcMat.Height / 2);

            // 泛洪填充
            Cv2.FloodFill(newSrcMat, seedPoint, Scalar.White, out _, new Scalar());
        }
    }

    public static void Hsv()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\箭头识别\e1.png", ImreadModes.Color);
        // Cv2.GaussianBlur(mat, mat, new Size(3, 3), 0);
        var splitMat = mat.Split();

        // 1. 红蓝通道按位与
        var red = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[0], new Scalar(250), new Scalar(255), red);
        var blue = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[2], new Scalar(0), new Scalar(10), blue);
        var andMat = new Mat(mat.Size(), MatType.CV_8UC1);

        Cv2.BitwiseAnd(red, blue, andMat);
        Cv2.ImShow("andMat2", andMat);

        // 寻找轮廓
        Cv2.FindContours(andMat, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Mat dst = Mat.Zeros(andMat.Size(), MatType.CV_8UC3);
        for (int i = 0; i < contours.Length; i++)
        {
            Cv2.DrawContours(dst, contours, i, Scalar.Red, 1, LineTypes.Link4, hierarchy);
        }

        Cv2.ImShow("寻找轮廓", dst);

        // 计算最大外接矩形

        if (contours.Length > 0)
        {
            var maxRect = Rect.Empty;
            var maxIndex = 0;
            for (int i = 0; i < contours.Length; i++)
            {
                var box = Cv2.BoundingRect(contours[i]);
                if (box.Width * box.Height > maxRect.Width * maxRect.Height)
                {
                    maxRect = box;
                    maxIndex = i;
                }
            }

            var maxContour = contours[maxIndex];

            // 计算轮廓的周长
            var perimeter = Cv2.ArcLength(maxContour, true);

            // 近似多边形拟合
            var approx = Cv2.ApproxPolyDP(maxContour, 0.08 * perimeter, true);

            // 如果拟合的多边形有三个顶点，认为是三角形
            if (approx.Length == 3)
            {
                // 在图像上绘制三角形的轮廓
                Cv2.DrawContours(mat, new Point[][] { approx }, -1, Scalar.Green, 1);

                // 剪裁出三角形所在区域
                var newSrcMat = new Mat(mat, maxRect);

                // HSV 阈值取出中心飞镖
                var hsvMat = new Mat();
                Cv2.CvtColor(newSrcMat, hsvMat, ColorConversionCodes.BGR2HSV);
                // var lowScalar = new Scalar(95, 255, 255);
                // var highScalar = new Scalar(255, 255, 255);
                var lowScalar = new Scalar(93, 155, 170);
                var highScalar = new Scalar(255, 255, 255);
                var hsvThresholdMat = new Mat();
                Cv2.InRange(hsvMat, lowScalar, highScalar, hsvThresholdMat);
                Cv2.ImShow("剪裁出三角形所在区域", hsvMat);
                Cv2.ImShow("HSV 阈值取出中心飞镖", hsvThresholdMat);

                // 循环计算三条边的中点,并计算中点到顶点的所有点中连续黑色像素的个数
                var maxBlackCount = 0;
                Point correctP1 = new(), correctP2 = new();
                var offset = new Point(maxRect.X, maxRect.Y);
                for (int i = 0; i < 3; i++)
                {
                    var midPoint = Midpoint(approx[i], approx[(i + 1) % 3]);
                    var targetPoint = approx[(i + 2) % 3];

                    // 中点到顶点的所有点
                    var lineIterator = new LineIterator(hsvThresholdMat, midPoint - offset, targetPoint - offset, PixelConnectivity.Connectivity8);

                    // 计算连续黑色像素的个数
                    var blackCount = 0;
                    foreach (var item in lineIterator)
                    {
                        if (item.GetValue<Vec2b>().Item0 == 255)
                        {
                            break;
                        }

                        blackCount++;
                    }

                    if (blackCount > maxBlackCount)
                    {
                        maxBlackCount = blackCount;
                        correctP1 = midPoint;
                        correctP2 = targetPoint;
                    }
                }
                Cv2.Line(mat, correctP1, correctP2 + (correctP2 - correctP1) * 3, Scalar.Red, 1);
                Cv2.ImShow("最终结果", mat);
            }
        }
    }
}
