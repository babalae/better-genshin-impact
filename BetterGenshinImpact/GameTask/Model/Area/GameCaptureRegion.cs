using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using System;
using System.Drawing;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Model.Area;

/// <summary>
/// 游戏捕获区域类
/// 主要用于转换到遮罩窗口的坐标
/// </summary>
public class GameCaptureRegion(Mat mat, int initX, int initY, Region? owner = null, INodeConverter? converter = null, DrawContent? drawContent = null) : ImageRegion(mat, initX, initY, owner, converter, drawContent)
{
    /// <summary>
    /// 在游戏捕获图像的坐标维度进行转换到遮罩窗口的坐标维度
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="w"></param>
    /// <param name="h"></param>
    /// <param name="pen"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    public RectDrawable ConvertToRectDrawable(int x, int y, int w, int h, Pen? pen = null, string? name = null)
    {
        var scale = TaskContext.Instance().DpiScale;
        System.Windows.Rect newRect = new(x / scale, y / scale, w / scale, h / scale);
        return new RectDrawable(newRect, pen, name);
    }

    /// <summary>
    /// 在游戏捕获图像的坐标维度进行转换到遮罩窗口的坐标维度
    /// </summary>
    /// <param name="x1"></param>
    /// <param name="y1"></param>
    /// <param name="x2"></param>
    /// <param name="y2"></param>
    /// <param name="pen"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    public LineDrawable ConvertToLineDrawable(int x1, int y1, int x2, int y2, Pen? pen = null, string? name = null)
    {
        var scale = TaskContext.Instance().DpiScale;
        var drawable = new LineDrawable(x1 / scale, y1 / scale, x2 / scale, y2 / scale);
        if (pen != null)
        {
            drawable.Pen = pen;
        }

        return drawable;
    }

    // public void DrawRect(int x, int y, int w, int h, Pen? pen = null, string? name = null)
    // {
    //     VisionContext.Instance().DrawContent.PutRect(name ?? "None", ConvertToRectDrawable(x, y, w, h, pen, name));
    // }

    /// <summary>
    /// 游戏窗口初始截图大于1080P的统一转换到1080P
    /// </summary>
    /// <returns></returns>
    public ImageRegion DeriveTo1080P()
    {
        if (Width <= 1920)
        {
            return this;
        }

        var scale = Width / 1920d;

        var newMat = new Mat();
        Cv2.Resize(SrcMat, newMat, new Size(1920, Height / scale));
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
