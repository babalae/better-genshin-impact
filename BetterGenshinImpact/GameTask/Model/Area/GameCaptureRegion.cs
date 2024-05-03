using BetterGenshinImpact.GameTask.Model.Area.Converter;
using BetterGenshinImpact.View.Drawable;
using System.Drawing;
using OpenCvSharp;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Model.Area;

/// <summary>
/// 游戏捕获区域类
/// 主要用于转换到遮罩窗口的坐标
/// </summary>
public class GameCaptureRegion(Bitmap bitmap, int initX, int initY, Region? owner = null, INodeConverter? converter = null) : ImageRegion(bitmap, initX, initY, owner, converter)
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
    /// 统一转换到1080P
    /// </summary>
    /// <returns></returns>
    public ImageRegion DeriveTo1080P()
    {
        if (Width == 1920)
        {
            return this;
        }
        var scale = Width / 1920d;

        var newMat = new Mat();
        Cv2.Resize(SrcMat, newMat, new Size(1920, 1080));
        _srcGreyMat?.Dispose();
        _srcMat?.Dispose();
        _srcBitmap?.Dispose();
        this.SrcBitmap.Dispose();
        return new ImageRegion(newMat, X, Y, this, new ScaleConverter(scale));
    }
}
