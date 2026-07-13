using System;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;

public static class SubPixMatch
{
    private static readonly Mat Features = Mat.FromPixelData(6, 9, MatType.CV_64F, new double[,]
    {
        {1/6.0, -(1/3.0), 1/6.0, 1/6.0, -(1/3.0), 1/6.0, 1/6.0, -(1/3.0), 1/6.0},
        {1/6.0, 1/6.0, 1/6.0, -(1/3.0), -(1/3.0), -(1/3.0), 1/6.0, 1/6.0, 1/6.0},
        {1/4.0, 0, -(1/4.0), 0, 0, 0, -(1/4.0), 0, 1/4.0},
        {-(1/6.0), 0, 1/6.0, -(1/6.0), 0, 1/6.0, -(1/6.0), 0, 1/6.0},
        {-(1/6.0), -(1/6.0), -(1/6.0), 0, 0, 0, 1/6.0, 1/6.0, 1/6.0},
        {-(1/9.0), 2/9.0, -(1/9.0), 2/9.0, 5/9.0, 2/9.0, -(1/9.0), 2/9.0, -(1/9.0)}
    });

    //读取最值点周围3x3区域进行二次拟合

    public static Point2f Fit(Mat src, Point loc)
    {
        // 参数校验
        if (src.Empty())
            throw new Exception("输入矩阵为空");
        if (src.Width < 3 || src.Height < 3)
            throw new Exception("输入矩阵过小");
        if (loc.X < 0 || loc.Y < 0 || loc.X >= src.Width || loc.Y >= src.Height)
            throw new Exception("输入点位超出范围");

        // 边界约束：确保3x3邻域有效
        var clampedX = Math.Clamp(loc.X, 1, src.Width - 2);
        var clampedY = Math.Clamp(loc.Y, 1, src.Height - 2);
        // 提取并预处理3x3邻域
        var neighborhoodMatrix = src[new Rect(clampedX - 1, clampedY - 1, 3, 3)]
            .Clone()
            .Reshape(0, 9); // 展平为9x1向量
        neighborhoodMatrix.ConvertTo(neighborhoodMatrix, MatType.CV_64FC1);

        // 计算拟合系数
        Mat coefficientMatrix = Features * neighborhoodMatrix;
        coefficientMatrix.GetArray<double>(out var coefficients);

        // 计算二次方程判别式
        var discriminant = coefficients[2] * coefficients[2] - 4 * coefficients[0] * coefficients[1];
        const double epsilon = 1e-20;
        if (Math.Abs(discriminant) < epsilon)
        {
            return loc;
        }
        // 计算偏移量并约束范围
        var offsetX = (2 * coefficients[1] * coefficients[3] - coefficients[2] * coefficients[4]) / discriminant;
        var offsetY = (2 * coefficients[0] * coefficients[4] - coefficients[2] * coefficients[3]) / discriminant;
        offsetX = Math.Clamp(offsetX, -1.0, 1.0);
        offsetY = Math.Clamp(offsetY, -1.0, 1.0);
        return new Point2f((float)(offsetX + clampedX), (float)(offsetY + clampedY));
    }
}