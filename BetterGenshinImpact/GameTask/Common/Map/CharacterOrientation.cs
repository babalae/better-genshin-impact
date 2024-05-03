using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map;

public class CharacterOrientation
{
    public static void Compute(CaptureContent content)
    {
        var mat = new Mat(content.CaptureRectArea.SrcMat, new Rect(62, 19, 212, 212));

        var splitMat = mat.Split();

        // 1. 红蓝通道按位与
        var red = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[0], new Scalar(250), new Scalar(255), red);
        var blue = new Mat(mat.Size(), MatType.CV_8UC1);
        Cv2.InRange(splitMat[2], new Scalar(0), new Scalar(10), blue);
        var andMat = new Mat(mat.Size(), MatType.CV_8UC1);

        Cv2.BitwiseAnd(red, blue, andMat);

        // 寻找轮廓
        Cv2.FindContours(andMat, out var contours, out var hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Mat dst = Mat.Zeros(andMat.Size(), MatType.CV_8UC3);

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
                        correctP1 = midPoint; // 底边中点
                        correctP2 = targetPoint; // 顶点
                    }
                }

                // VisionContext.Instance().DrawContent.PutLine("co", new LineDrawable(correctP1, correctP2 + (correctP2 - correctP1) * 3));
            }
        }
    }

    static Point Midpoint(Point p1, Point p2)
    {
        var midX = (p1.X + p2.X) / 2;
        var midY = (p1.Y + p2.Y) / 2;
        return new Point(midX, midY);
    }
}
