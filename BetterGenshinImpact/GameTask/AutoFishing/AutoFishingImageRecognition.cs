using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vision.Recognition;
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
            try
            {
                using var mask = new Mat();
                using var rgbMat = new Mat();

                Cv2.CvtColor(src, rgbMat, ColorConversionCodes.BGR2RGB);
                var lowPurple = new Scalar(255, 255, 192);
                var highPurple = new Scalar(255, 255, 192);
                Cv2.InRange(rgbMat, lowPurple, highPurple, mask);
                Cv2.Threshold(mask, mask, 0, 255, ThresholdTypes.Binary); //二值化

                Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External,
                    ContourApproximationModes.ApproxSimple, null);
                if (contours.Length > 0)
                {
                    var boxes = contours.Select(Cv2.BoundingRect).Where(w => w.Height >= 10);
                    return boxes.ToList();
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }

            return null;
        }

        /// <summary>
        /// 匹配 “鱼儿上钩拉！”文字区域
        /// </summary>
        /// <param name="src"></param>
        /// <param name="liftingWordsAreaRect"></param>
        /// <returns></returns>
        public static Rect MatchFishBiteWords(Mat src, Rect liftingWordsAreaRect)
        {
            try
            {
                Cv2.CvtColor(src, src, ColorConversionCodes.BGR2RGB);
                var lowPurple = new Scalar(253, 253, 253);
                var highPurple = new Scalar(255, 255, 255);
                Cv2.InRange(src, lowPurple, highPurple, src);
                Cv2.Threshold(src, src, 0, 255, ThresholdTypes.Binary);
                var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(20, 20),
                    new OpenCvSharp.Point(-1, -1));
                Cv2.Dilate(src, src, kernel); //膨胀

                Cv2.FindContours(src, out var contours, out _, RetrievalModes.External,
                    ContourApproximationModes.ApproxSimple, null);
                if (contours.Length > 0)
                {
                    var boxes = contours.Select(Cv2.BoundingRect);
                    var rects = boxes.ToList();
                    if (rects.Count > 1)
                    {
                        rects.Sort((a, b) => b.Height.CompareTo(a.Height));
                    }

                    //VisionContext.Instance().DrawContent.PutRect("FishBiteTipsDebug",
                    //    rects[0].ToWindowsRectangleOffset(liftingWordsAreaRect.X, liftingWordsAreaRect.Y)
                    //        .ToRectDrawable());
                    if (rects[0].Height < src.Height
                        && rects[0].Width * 1.0 / rects[0].Height >= 3 // 长宽比判断
                        && liftingWordsAreaRect.Width > rects[0].Width * 3 // 文字范围3倍小于钓鱼条范围的
                        && liftingWordsAreaRect.Width * 1.0 / 2 > rects[0].X // 中轴线判断左
                        && liftingWordsAreaRect.Width * 1.0 / 2 < rects[0].X + rects[0].Width) // 中轴线判断右
                    {
                        return rects[0];
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }

            return Rect.Empty;
        }
    }
}