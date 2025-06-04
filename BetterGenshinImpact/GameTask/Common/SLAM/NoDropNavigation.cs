using System;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.Common.SLAM;

public class NoDropNavigation
{
    public static Rect PersonReferenceRoi { get; set; } = new Rect(883, 823, 167, 70); // x, y, width, height (脚部区域)
    public static Rect ForwardDetectionRoiL { get; set; } = new Rect(781, 435, 77, 180); // x, y, width, height (左侧区域)
    public static Rect ForwardDetectionRoiR { get; set; } = new Rect(1139, 435, 77, 180); // x, y, width, height (右侧区域)
    public float ObstacleDepthThreshold { get; set; } = 2.5f;

    /// <summary>
    /// 能否往前走
    /// </summary>
    /// <param name="depthMat"></param>
    /// <returns>-1左转，0直行，1右转</returns>
    public int? CanGoForward(Mat depthMat)
    {
        Cv2.Resize(depthMat, depthMat, new Size(1920, 1080));
        var personReference = new Mat(depthMat, PersonReferenceRoi);
        var forwardDetectionL = new Mat(depthMat, ForwardDetectionRoiL);
        var forwardDetectionR = new Mat(depthMat, ForwardDetectionRoiR);

        var personValue = personReference.Mean().ToDouble();
        var forwardDetectionLValue = forwardDetectionL.Mean().ToDouble();
        var forwardDetectionRValue = forwardDetectionR.Mean().ToDouble();
        
        var dl = forwardDetectionLValue - personValue;
        var dr = forwardDetectionRValue - personValue;
        
        Logger.LogInformation("得到地形差：(左{dl}, 右{dr})", dl, dr);
        
        if (dl > ObstacleDepthThreshold && dr > ObstacleDepthThreshold)
            return null;
        else if (dl < ObstacleDepthThreshold && dr < ObstacleDepthThreshold)
            return 0;
        else if (dl < ObstacleDepthThreshold && dr > ObstacleDepthThreshold)
            return -1;
        else
            return 1;
    }
}