using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.Core.Mask;
using OpenCvSharp;
using System;
using System.Drawing;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Model.Area;

/// <summary>
/// 游戏捕获区域类
/// 主要用于转换到遮罩窗口的坐标
/// </summary>
public class GameCaptureRegion(Mat mat, int initX, int initY, Region? owner = null, INodeConverter? converter = null, IMaskWindowDrawingBoard? drawingBoard = null) : ImageRegion(mat, initX, initY, owner, converter, drawingBoard)
{
    /// <summary>
    /// 游戏窗口初始截图大于1080P的统一转换到1080P
    /// </summary>
    /// <returns></returns>
    public ImageRegion DeriveTo1080P()
    {
        if (Width <= 1920 && Height <= 1080)
        {
            return this;
        }

        // 取宽、高相对于 1920x1080 参考画布的缩放比中的较小值，保持原始宽高比不失真；
        // 非 16:9 分辨率下较大的一边会超出参考画布尺寸，而不是被压扁匹配参考宽度。
        var scale = Math.Min(Width / 1920d, Height / 1080d);

        var newMat = new Mat();
        Cv2.Resize(SrcMat, newMat, new Size(Width / scale, Height / scale));
        Dispose();
        return new ImageRegion(newMat, 0, 0, this, new ScaleConverter(scale));
        // return new ImageRegion(newMat, 0, 0, this, new TranslationConverter(0, 0));
    }

    /// <summary>
    /// 静态方法,在游戏窗体捕获区域维度进行点击
    /// </summary>
    /// <param name="posFunc">
    /// 实现一个方法输出要点击的的坐标(相对游戏捕获区域内坐标),提供以下参数供计算使用
    /// Size = 当前游戏捕获区域大小
    /// double = 当前游戏捕获区域到 1080P 的缩放比例
    /// 也就是说方法内的魔法数字必须是 1080P下的数字
    /// </param>
    public static void GameRegionClick(Func<Size, double, (double, double)> posFunc)
    {
        var captureAreaRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
        var (cx, cy) = posFunc(new Size(captureAreaRect.Width, captureAreaRect.Height), assetScale);
        DesktopRegion.DesktopRegionClick(captureAreaRect.X + cx, captureAreaRect.Y + cy);
    }

    public static void GameRegionMove(Func<Size, double, (double, double)> posFunc)
    {
        var captureAreaRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
        var (cx, cy) = posFunc(new Size(captureAreaRect.Width, captureAreaRect.Height), assetScale);
        DesktopRegion.DesktopRegionMove(captureAreaRect.X + cx, captureAreaRect.Y + cy);
    }

    public static void GameRegionMoveBy(Func<Size, double, (double, double)> deltaFunc)
    {
        var captureAreaRect = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.ScaleTo1080PRatio;
        var (dx, dy) = deltaFunc(new Size(captureAreaRect.Width, captureAreaRect.Height), assetScale);
        DesktopRegion.DesktopRegionMoveBy(dx, dy);
    }

    /// <summary>
    /// 静态方法,输入1080P下的坐标,方法会自动转换到当前游戏捕获区域大小下的坐标并点击
    /// </summary>
    /// <param name="cx"></param>
    /// <param name="cy"></param>
    public static void GameRegion1080PPosClick(double cx, double cy)
    {
        // 1080P坐标 转换到实际游戏窗口坐标
        GameRegionClick((_, scale) => (cx * scale, cy * scale));
    }

    public static void GameRegion1080PPosMove(double cx, double cy)
    {
        // 1080P坐标 转换到实际游戏窗口坐标
        GameRegionMove((_, scale) => (cx * scale, cy * scale));
    }
}
