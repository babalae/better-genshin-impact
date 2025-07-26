using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using Tensorboard;
using Windows.Storage;


namespace GameTask.AutoFight;
public class findEnemyTask
{
    public static List<Tuple<Point, Point>> FindAll(ImageRegion ra)
    {
        // 遍历整个图像 寻找 连续的 10 个 (255,90,90) 像素
        Scalar  targetColor = new Scalar(90, 90, 255); // B=90, G=90, R=255
        int tolerance = 10;  // 像素值偏差
        int consecutiveCount = 10; // 连续个数
        Mat SrcMat = ra.SrcMat;

        // 使用 SrcMat（彩色图像）进行检测
        if (SrcMat == null || SrcMat.Empty())
            return new List<Tuple<Point, Point>>();

        List<Tuple<Point, Point>> resultPoints = new List<Tuple<Point, Point>>();
        int width = SrcMat.Width;
        int height = SrcMat.Height;
        int consecutiveFound = 0;
        // 离散扫描参数
        int xStep = 5;  // 横向间隔
        int yStep = 2;  // 纵向间隔
        int minWidth = 10;
        int minHeight = 5;

        int startY = 0;
        int endY = height;

        int startX = 0;
        int endX = (int)(width * 0.9); // 避免识别到自己血条
        // 创建一个标记矩阵，记录已处理的像素（0=未处理，1=已处理）
        Mat marked = new Mat(SrcMat.Rows, SrcMat.Cols, MatType.CV_8UC1, Scalar.All(0));
        for (int y = startY; y < endY; y += yStep)
        {
            // 每行起始X偏移，实现横向离散扫描
            int xStart = (y / yStep) % xStep + startX;

            for (int x = xStart; x < endX; x += xStep)
            {
                // 如果当前像素已被标记，跳过
                if (marked.At<byte>(y, x) == 1)
                    continue;
                Vec3b pixel = SrcMat.At<Vec3b>(y, x);

                // 检查是否在目标颜色范围内
                bool isMatch = IsColorMatch(pixel, targetColor, tolerance);
                if (isMatch)
                {
                    // 找到当前像素的边界
                    FindBoundary(SrcMat, x, y, targetColor, tolerance, out Point topLeft, out Point bottomRight);

                    // 检查区域尺寸是否满足条件
                    int regionWidth = bottomRight.X - topLeft.X;
                    int regionHeight = bottomRight.Y - topLeft.Y;

                    if (regionWidth >= minWidth && regionHeight >= minHeight)
                    {

                        if (regionWidth >= minWidth && regionHeight >= minHeight)
                        {
                            resultPoints.Add(new Tuple<Point, Point>(topLeft, bottomRight));

                            // 标记整个区域为已处理
                            for (int yy = topLeft.Y; yy <= bottomRight.Y; yy++)
                            {
                                for (int xx = topLeft.X; xx <= bottomRight.X; xx++)
                                {
                                    if (xx < marked.Cols && yy < marked.Rows)
                                        marked.At<byte>(yy, xx) = 1;
                                }
                            }

                            // 直接跳到右边界
                            x = bottomRight.X + 1;
                        }
                    }
                }
            }
        }
        return resultPoints;
    }

    private static void FindBoundary(Mat srcMat, int startX, int startY, Scalar targetColor, int tolerance, out Point topLeft, out Point bottomRight)
    {
        int width = srcMat.Width;
        int height = srcMat.Height;

        // 初始化边界
        int left = startX;
        int right = startX;
        int top = startY;
        int bottom = startY;

        // 横向向左扫描找左边界
        while (left >= 0 && IsColorMatch(srcMat.At<Vec3b>(startY, left), targetColor, tolerance))
        {
            left--;
        }
        left = Math.Max(0, left + 1);

        // 横向向右扫描找右边界
        while (right < width && IsColorMatch(srcMat.At<Vec3b>(startY, right), targetColor, tolerance))
        {
            right++;
        }
        right = Math.Min(width - 1, right - 1);

        // 纵向向上扫描找上边界
        while (top >= 0 && IsColorMatch(srcMat.At<Vec3b>(top, startX), targetColor, tolerance))
        {
            top--;
        }
        top = Math.Max(0, top + 1);

        // 纵向向下扫描找下边界
        while (bottom < height && IsColorMatch(srcMat.At<Vec3b>(bottom, startX), targetColor, tolerance))
        {
            bottom++;
        }
        bottom = Math.Min(height - 1, bottom - 1);

        topLeft = new Point(left, top);
        bottomRight = new Point(right, bottom);
    }


    private static bool IsColorMatch(Vec3b pixel, Scalar targetColor, int tolerance)
    {
        return Math.Abs(pixel.Item0 - targetColor.Val0) <= tolerance &&  // B
               Math.Abs(pixel.Item1 - targetColor.Val1) <= tolerance &&  // G
               Math.Abs(pixel.Item2 - targetColor.Val2) <= tolerance;    // R
    }


    public static Point getCenter(Point item1,Point item2)
    {
        return new Point((item1.X + item2.X) / 2, (item1.Y + item2.Y) / 2);
    }

    public static Point getCenter(Tuple<Point, Point> item)
    {
        return getCenter(item.Item1, item.Item2);
    }

    public static bool AimToCenterEnemy(ImageRegion ra)
    {
        List<Tuple<Point, Point>> enemys = FindAll(ra);
        Point center = new Point(0, 0);
        foreach (var enemy in enemys)
        {
            if (center.X == 0) { center = getCenter(enemy); }
            else
            {
                int middle = ra.X + (ra.Width / 2);
                int currentCenter = (enemy.Item1.X + enemy.Item2.X) / 2;
                int oldCenter = center.X;
                if (Math.Abs(oldCenter - middle) > Math.Abs(currentCenter - middle))
                {
                    center = findEnemyTask.getCenter(enemy);
                }
            }
        }
        if (center.X != 0)
        {
            Simulation.SendInput.Mouse.MoveMouseBy(center.X - ra.Width / 2, 0);
            return true;
        }
        return false;
    } 
}
