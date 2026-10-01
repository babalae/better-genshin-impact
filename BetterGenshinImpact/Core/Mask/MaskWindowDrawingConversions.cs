namespace BetterGenshinImpact.Core.Mask;

public static class MaskWindowDrawingConversions
{
    /// <summary>
    /// 捕获像素坐标系下的 OpenCV 矩形转换为遮罩窗口绘制矩形。
    /// 只能用于坐标已经是整个游戏捕获区域的矩形；子区域内的坐标请用 Region.ToMaskWindowDrawingRect
    /// </summary>
    public static MaskWindowDrawingRect ToMaskWindowDrawingRect(this OpenCvSharp.Rect rect, System.Drawing.Pen? pen = null)
    {
        return new MaskWindowDrawingRect(
            new System.Windows.Rect(rect.X, rect.Y, rect.Width, rect.Height),
            MaskWindowDrawingStroke.FromPen(pen));
    }
}
