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
        System.Windows.Rect newRect = new((int)(x / scale), (int)(y / scale), (int)(w / scale), (int)(h / scale));
        return new RectDrawable(newRect, pen, name);
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
        Cv2.Resize(SrcMat, newMat, new Size(1920, 1090));
        var child = new ImageRegion(newMat, X, Y, this, new ScaleConverter(scale));
        return AddChild(child);
    }
}
