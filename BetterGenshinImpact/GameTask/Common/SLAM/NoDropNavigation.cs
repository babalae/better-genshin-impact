using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Common.SLAM;

public class NoDropNavigation
{
    // 假设整个画面是 1920x1080
    private const int ScreenWidth = 1920;
    private const int ScreenHeight = 1080;

    // 人物参考区域 (用于获取人物脚下的基准深度)
    // 考虑到 PersonReferenceRoi 很大 (655, 349, 613, 514)，
    // 我们需要从其中选取一个更小的、更可靠的地面区域作为基准。
    public static Rect PersonReferenceRoi { get; set; } = new Rect(841, 323, 238, 434);

    // 定义一个更精确的地面参考区域，位于人物脚下
    private readonly Rect GroundReferenceRoi = new Rect(
        PersonReferenceRoi.X + PersonReferenceRoi.Width / 2 - 50, // 中心偏左
        PersonReferenceRoi.Y + PersonReferenceRoi.Height - 30,    // 底部向上30像素
        100, // 宽度
        20   // 高度
    );

    // 前方左右探测区域 (窄条，用于线性拟合)
    private readonly Rect ForwardGroundLeftRoi = new Rect(
        ScreenWidth / 2 - 200, // 屏幕中心偏左
        ScreenHeight / 2 - 150, // 屏幕中心偏下，代表地面
        30, // 宽度
        300   // 高度 (窄条)
    );

    private readonly Rect ForwardGroundRightRoi = new Rect(
        ScreenWidth / 2 + 200, // 屏幕中心偏右
        ScreenHeight / 2 - 150, // 屏幕中心偏下，代表地面
        30, // 宽度
        300   // 高度 (窄条)
    );

    // --- 可配置的阈值 ---
    // 深度差阈值：
    // 如果 (前方区域平均深度 - 参考深度) < ObstacleDepthThreshold，则视为障碍物 (负值表示前方更近)
    private const double ObstacleDepthThreshold = -15.0; 
    // 如果 (前方区域平均深度 - 参考深度) > DropOffDepthThreshold，则视为深渊/空旷 (正值表示前方更远)
    private const double DropOffDepthThreshold = 15.0; 

    // 坡度阈值：
    // 如果 Math.Abs(slope) < FlatSlopeThreshold，则视为平坦
    private const double FlatSlopeThreshold = 0.03; // 坡度绝对值小于此值视为平坦
    // 如果 slope < SteepDownhillSlopeThreshold，则视为陡峭下坡/深渊 (负值)
    private const double SteepDownhillSlopeThreshold = -0.1; 
    // 如果 slope > SteepUphillSlopeThreshold，则视为陡峭上坡/障碍 (正值)
    private const double SteepUphillSlopeThreshold = 0.1; 
    // --- 可配置的阈值结束 ---

    /// <summary>
    /// 判断是否可以向前移动，并给出建议的转向，基于地形深度曲线的线性拟合。
    /// </summary>
    /// <param name="depthMat">输入的深度图。</param>
    /// <returns>
    /// 0: 可以直走 (左右都畅通)
    /// 1: 建议向左转 (右侧有障碍/深渊，左侧畅通)
    /// -1: 建议向右转 (左侧有障碍/深渊，右侧畅通)
    /// null: 无法前进 (左右两侧都有障碍或深渊，或一侧障碍一侧深渊)
    /// </returns>
    public int? CanGoForward(Mat depthMat)
    {
        if (depthMat == null || depthMat.Empty())
        {
            Logger.LogInformation("深度图无效或为空。");
            return null;
        }

        // 确保深度图分辨率正确，如果不是，则进行 Resize
        if (depthMat.Width != ScreenWidth || depthMat.Height != ScreenHeight)
        {
            Cv2.Resize(depthMat, depthMat, new Size(ScreenWidth, ScreenHeight));
        }

        // 获取人物脚下的基准深度
        double groundReferenceDepth;
        using (var groundRefMat = new Mat(depthMat, GroundReferenceRoi))
        {
            // 使用中位数而不是平均值，对噪声和少量非地面像素更鲁棒
            // 或者，如果确定 ROI 纯粹是地面，Mean() 也可以
            groundReferenceDepth = GetMedianDepth(groundRefMat); 
            if (double.IsNaN(groundReferenceDepth))
            {
                Logger.LogInformation("无法获取地面参考深度，可能ROI为空或无效。");
                return null;
            }
        }
        Logger.LogInformation($"基准地面深度: {groundReferenceDepth:F2}");

        // 获取前方左右区域的线性拟合参数
        (double slopeL, double interceptL) = GetLinearFitParameters(depthMat, ForwardGroundLeftRoi);
        (double slopeR, double interceptR) = GetLinearFitParameters(depthMat, ForwardGroundRightRoi);

        Logger.LogInformation($"左侧地形: 斜率={slopeL:F3}, 截距={interceptL:F2}");
        Logger.LogInformation($"右侧地形: 斜率={slopeR:F3}, 截距={interceptR:F2}");

        // 判断左右路径状态
        PathStatus statusL = GetPathStatus(slopeL, interceptL, groundReferenceDepth);
        PathStatus statusR = GetPathStatus(slopeR, interceptR, groundReferenceDepth);

        Logger.LogInformation($"左侧状态: {statusL}, 右侧状态: {statusR}");

        // 根据左右路径状态进行决策
        if (statusL == PathStatus.Clear && statusR == PathStatus.Clear)
        {
            return 0; // 左右都畅通，可以直走
        }
        else if ((statusL == PathStatus.Obstacle || statusL == PathStatus.DropOff) && statusR == PathStatus.Clear)
        {
            return -1; // 左侧有障碍/深渊，右侧畅通，建议向右转
        }
        else if (statusL == PathStatus.Clear && (statusR == PathStatus.Obstacle || statusR == PathStatus.DropOff))
        {
            return 1; // 右侧有障碍/深渊，左侧畅通，建议向左转
        }
        else // 左右两侧都存在障碍或深渊
        {
            return null; // 无法前进
        }
    }

    /// <summary>
    /// 对指定 ROI 内的深度数据进行线性拟合，返回斜率和截距。
    /// </summary>
    /// <param name="depthMat">完整的深度图。</param>
    /// <param name="roi">要进行拟合的矩形区域。</param>
    /// <returns>包含斜率 (m) 和截距 (c) 的元组。</returns>
    private (double slope, double intercept) GetLinearFitParameters(Mat depthMat, Rect roi)
    {
        using var roiMat = new Mat(depthMat, roi);
        if (roiMat.Empty())
        {
            return (double.NaN, double.NaN); // ROI为空
        }

        // 提取 (x_relative, depth) 点
        var points = new List<Point2f>();
        for (int y = 0; y < roiMat.Rows; y++)
        {
            for (int x = 0; x < roiMat.Cols; x++)
            {
                float depthValue = roiMat.At<float>(y, x); // 深度图是 float 类型
                
                if (depthValue > 0) // 忽略无效深度值 (0或非常大)
                {
                    points.Add(new Point2f(x, depthValue)); // x是相对坐标，y是深度
                }
            }
        }

        if (points.Count < 2) // 至少需要两个点才能拟合直线
        {
            return (double.NaN, double.NaN);
        }

        // 最小二乘法线性回归
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        foreach (var p in points)
        {
            sumX += p.X;
            sumY += p.Y;
            sumXY += p.X * p.Y;
            sumX2 += p.X * p.X;
        }

        int n = points.Count;
        double denominator = (n * sumX2 - sumX * sumX);

        if (Math.Abs(denominator) < 1e-6) // 避免除以零，表示所有X值都相同 (垂直线)
        {
            return (double.NaN, double.NaN); // 无法拟合有效斜率
        }

        double slope = (n * sumXY - sumX * sumY) / denominator;
        double intercept = (sumY - slope * sumX) / n;

        return (slope, intercept);
    }

    /// <summary>
    /// 根据深度差值和坡度判断路径状态。
    /// </summary>
    /// <param name="slope">区域的拟合斜率。</param>
    /// <param name="intercept">区域的拟合截距（代表该区域的平均深度）。</param>
    /// <param name="referenceDepth">人物脚下的基准深度。</param>
    /// <returns>路径状态枚举。</returns>
    private PathStatus GetPathStatus(double slope, double intercept, double referenceDepth)
    {
        if (double.IsNaN(slope) || double.IsNaN(intercept))
        {
            return PathStatus.Unknown; // 无法获取有效数据
        }

        // 1. 基于平均深度差判断
        var depthDiff = intercept - referenceDepth;
        bool isObstacleByDepth = depthDiff < ObstacleDepthThreshold;
        bool isDropOffByDepth = depthDiff > DropOffDepthThreshold;

        // 2. 基于坡度判断
        bool isSteepUphill = slope > SteepUphillSlopeThreshold;
        bool isSteepDownhill = slope < SteepDownhillSlopeThreshold;
        bool isFlatSlope = Math.Abs(slope) < FlatSlopeThreshold;

        // 综合判断
        if (isObstacleByDepth || isSteepUphill)
        {
            return PathStatus.Obstacle; // 前方太近或陡峭上坡
        }
        else if (isDropOffByDepth || isSteepDownhill)
        {
            return PathStatus.DropOff; // 前方太远或陡峭下坡/深渊
        }
        else if (isFlatSlope && !isObstacleByDepth && !isDropOffByDepth)
        {
            return PathStatus.Clear; // 坡度平坦且深度适中
        }
        else
        {
            // 介于平坦和障碍/深渊之间，或者坡度不平坦但深度差不明显
            // 这种情况可以根据实际需求调整，例如视为“不确定”或“轻微障碍”
            return PathStatus.Uncertain; 
        }
    }

    /// <summary>
    /// 获取 Mat 中所有非零像素的中位数深度值。
    /// </summary>
    /// <param name="mat">输入的 Mat。</param>
    /// <returns>中位数深度值，如果 Mat 为空或无有效像素则返回 NaN。</returns>
    private double GetMedianDepth(Mat mat)
    {
        if (mat.Empty())
        {
            return double.NaN;
        }

        var depths = new List<float>();
        for (int y = 0; y < mat.Rows; y++)
        {
            for (int x = 0; x < mat.Cols; x++)
            {
                float depthValue = mat.At<float>(y, x); // 假设深度图是 float 类型
                // 如果是 ushort 类型，使用 ushort depthValue = mat.At<ushort>(y, x);
                if (depthValue > 0) // 忽略无效深度值
                {
                    depths.Add(depthValue);
                }
            }
        }

        if (depths.Count == 0)
        {
            return double.NaN;
        }

        depths.Sort();
        if (depths.Count % 2 == 1)
        {
            return depths[depths.Count / 2];
        }
        else
        {
            return (depths[depths.Count / 2 - 1] + depths[depths.Count / 2]) / 2.0;
        }
    }

    // 定义一个枚举来表示路径状态，提高可读性
    private enum PathStatus
    {
        Clear,      // 畅通无阻
        Obstacle,   // 有障碍物 (前方比参考点近 或 陡峭上坡)
        DropOff,    // 有深渊或大片空旷 (前方比参考点远 或 陡峭下坡)
        Uncertain,  // 介于两者之间，需要进一步判断或谨慎
        Unknown     // 无法获取数据
    }
}