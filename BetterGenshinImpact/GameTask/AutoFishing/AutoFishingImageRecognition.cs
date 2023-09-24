using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vision.Recognition.Helper.OpenCv;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    public class AutoFishingImageRecognition
    {
        /// <summary>
        /// 钓鱼条矩形识别
        /// </summary>
        /// <param name="src"></param>
        /// <returns></returns>
        public static List<Rect>? GetFishBarRect(Mat src)
        {
            using var mask = new Mat();
            using var rgbMat = new Mat();

            Cv2.CvtColor(src, rgbMat, ColorConversionCodes.BGR2RGB);
            var lowPurple = new Scalar(255, 255, 192);
            var highPurple = new Scalar(255, 255, 192);
            Cv2.InRange(rgbMat, lowPurple, highPurple, mask);
            Cv2.Threshold(mask, mask, 0, 255, ThresholdTypes.Binary); //二值化

            Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple, null);
            if (contours.Length > 0)
            {
                var boxes = contours.Select(Cv2.BoundingRect).Where(w => w.Height >= 10);
                return boxes.ToList();
            }
            return null;
        }
    }
}
