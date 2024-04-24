using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

public class CommonRecognition
{
    /// <summary>
    ///     寻找游戏内按钮
    /// </summary>
    /// <param name="srcMat"></param>
    /// <returns></returns>
    public static List<Rect> FindGameButton(Mat srcMat)
    {
        try
        {
            var src = srcMat.Clone();
            Cv2.CvtColor(src, src, ColorConversionCodes.BGR2RGB);
            var lowPurple = new Scalar(236, 229, 216);
            var highPurple = new Scalar(236, 229, 216);
            Cv2.InRange(src, lowPurple, highPurple, src);
            Cv2.Threshold(src, src, 0, 255, ThresholdTypes.Binary);
            //var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(20, 20),
            //    new OpenCvSharp.Point(-1, -1));
            //Cv2.Dilate(src, src, kernel); //膨胀

            Cv2.FindContours(src, out var contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);
            if (contours.Length > 0)
            {
                var boxes = contours.Select(Cv2.BoundingRect).Where(r => r.Width > 50);
                return boxes.ToList();
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        return [];
    }
}
