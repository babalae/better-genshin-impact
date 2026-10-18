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
    }
}
