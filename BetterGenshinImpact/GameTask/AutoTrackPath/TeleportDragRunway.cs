using System;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 传送快速拖动“边缘感知动态跑道截断”纯计算。
/// 仅在 Dynamic_Runway_Mode（快速拖动 && MapZoomDistanceForce==0）由 MoveMapTo 调用。
/// 详见 .kiro/specs/teleport-drag-edge-aware-runway-clamp/。
/// </summary>
public static class TeleportDragRunway
{
    /// <summary>
    /// 计算落点沿拖动方向到捕获区边缘、预留安全边距后的可移动跑道，
    /// 返回施加于位移向量的等比缩放因子 t ∈ [0, 1]。最终位移 = 原位移 * t，方向不变（整体等比）。
    /// </summary>
    /// <param name="landingX">落点 X（捕获区像素，0..width）</param>
    /// <param name="landingY">落点 Y（捕获区像素，0..height）</param>
    /// <param name="dispX">本次期望的 X 方向位移（捕获区像素，含符号）</param>
    /// <param name="dispY">本次期望的 Y 方向位移（捕获区像素，含符号）</param>
    /// <param name="width">捕获区宽度</param>
    /// <param name="height">捕获区高度</param>
    /// <param name="safetyMargin">安全边距，默认 50px</param>
    /// <returns>缩放因子 t：最终位移 = disp * t，t ∈ [0,1]</returns>
    public static double ComputeRunwayScale(
        double landingX, double landingY,
        double dispX, double dispY,
        double width, double height,
        double safetyMargin = 50)
    {
        // P6 零向量安全：无位移，返回 1.0（disp*1 == 0，无除零）
        if (dispX == 0 && dispY == 0)
        {
            return 1.0;
        }

        double t = 1.0;

        // X 轴：仅当有 X 分量时才受 X 跑道约束（某轴分量为 0 不施加限制）
        if (dispX != 0)
        {
            // 沿拖动方向到边缘的距离：正向到 width，负向到 0
            double edgeDist = dispX > 0 ? (width - landingX) : landingX;
            double runway = edgeDist - safetyMargin;
            if (runway < 0)
            {
                runway = 0; // P7 跑道非负
            }

            double axisT = runway / Math.Abs(dispX); // 该轴允许的最大比例
            if (axisT < t)
            {
                t = axisT;
            }
        }

        // Y 轴同理
        if (dispY != 0)
        {
            double edgeDist = dispY > 0 ? (height - landingY) : landingY;
            double runway = edgeDist - safetyMargin;
            if (runway < 0)
            {
                runway = 0;
            }

            double axisT = runway / Math.Abs(dispY);
            if (axisT < t)
            {
                t = axisT;
            }
        }

        // P4 界内不变：axisT >= 1 时保持 1，不放大
        if (t > 1.0)
        {
            t = 1.0;
        }

        if (t < 0.0)
        {
            t = 0.0;
        }

        return t;
    }
}
