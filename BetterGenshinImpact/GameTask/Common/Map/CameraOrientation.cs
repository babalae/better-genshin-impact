using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Camera;
using BetterGenshinImpact.GameTask.Common.Map.MiniMap;
using Point = OpenCvSharp.Point;

namespace BetterGenshinImpact.GameTask.Common.Map;

public class CameraOrientation
{
    private static readonly MiniMapPreprocessor _coV2 = new();
    
    /// <summary>
    /// 计算当前小地图摄像机朝向的角度
    /// </summary>
    /// <param name="mat">完整游戏截图彩色</param>
    /// <returns>角度</returns>
    public static float Compute(Mat mat)
    {
        var mimiMap = new Mat(mat, MapAssets.Instance.MimiMapRect);
        return ComputeMiniMap(mimiMap);
    }

    /// <summary>
    /// 计算当前小地图摄像机朝向的角度
    /// </summary>
    /// <param name="mat">小地图彩色图</param>
    /// <returns>角度</returns>
    public static float ComputeMiniMap(Mat mat)
    {
       var (angle, confidence)  = _coV2.PredictRotationWithConfidence(mat);
       if (confidence < 0.2)
       {
           Debug.WriteLine($"置信度过低, {confidence}<0.2, 不可靠视角 {angle}");
           Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2GRAY);
           return CameraOrientationFromGia.ComputeMiniMap(mat);
       }
       return angle;
    }

    public static void DrawDirection(ImageRegion region, double angle, string name = "camera", Pen? pen = null)
    {
        // 绘图
        var scale = TaskContext.Instance().SystemInfo.AssetScale;
        const int r = 100;
        var center = new Point(168 * scale, 125 * scale); // 地图中心点 后续建议调整
        var x1 = center.X + r * Math.Cos(angle * Math.PI / 180);
        var y1 = center.Y + r * Math.Sin(angle * Math.PI / 180);

        // var line = new LineDrawable(center, new Point(x1, y1))
        // {
        //     Pen = new Pen(Color.Yellow, 1)
        // };
        // VisionContext.Instance().DrawContent.PutLine("camera", line);

        pen ??= new Pen(Color.Yellow, 1);

        region.DrawLine(center.X, center.Y, (int)x1, (int)y1, name, pen);
    }
}
