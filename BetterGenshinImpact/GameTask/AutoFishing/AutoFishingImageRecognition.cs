using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
                // 拉条框的黄色是：RGB 255, 255, 192 ~ HSV 43, 63, 255
                // var testPixel = src.At<Vec3b>(105, 968); // 注意是（Y，X）
                // using Mat rgbMat = src.CvtColor(ColorConversionCodes.BGR2HSV_FULL);
                // var testPixelHSV = rgbMat.At<Vec3b>(105, 968);   // 注意是（Y，X）
                Scalar hsv = OpenCvCommonHelper.CommonHSV2OpenCVHSVFull(new Scalar(60, 0.25, 1.00));
                var lowYellow = new Scalar(hsv.Val0 - 3, hsv.Val1 - 20, hsv.Val2 - 10);
                var highYellow = new Scalar(hsv.Val0 + 3.5, hsv.Val1 + 40, hsv.Val2);
                using Mat mask = OpenCvCommonHelper.InRangeHsvFull(src, lowYellow, highYellow);

                using Mat threshold = mask.Threshold(0, 255, ThresholdTypes.Binary); //二值化

                Cv2.FindContours(threshold, out var contours, out _, RetrievalModes.External,
                    ContourApproximationModes.ApproxSimple, null);
                if (contours.Length > 0)
                {
                    contours = contours.Where(c => Cv2.MinAreaRect(c).Angle % 45 <= 1).ToArray();  // 剔除倾斜的；箭头边缘是45度角，在游标靠近两侧箭头时，箭头的最小外接是45度的
                    List<Rect> boxes = contours.Select(Cv2.BoundingRect).ToList();
                    Rect widest = boxes.OrderBy(b => b.Width).LastOrDefault();  // 取最宽的一根当作基准
                    if (widest == default)
                    {
                        return null;
                    }
                    boxes = boxes.Where(r => Math.Abs((widest.Y + widest.Height / 2) - (r.Y + r.Height / 2)) < widest.Height / 5)   // 保持一条水平线
                        .Where(r => Math.Abs(widest.Height - r.Height) < (widest.Height / 3) && r.Width > (widest.Height / 4)).ToList();  // 剔除高度差异太大的，和宽度太小的
                    return boxes;
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
        public static Rect? MatchFishBiteWords(Mat src, Rect liftingWordsAreaRect)
        {
            using Mat rgb = src.CvtColor(ColorConversionCodes.BGR2RGB);
            var lowPurple = new Scalar(253, 253, 253);
            var highPurple = new Scalar(255, 255, 255);
            using Mat purple = rgb.InRange(lowPurple, highPurple);
            using Mat threshold = purple.Threshold(0, 255, ThresholdTypes.Binary);
            var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(20, 20),
                new Point(-1, -1));
            using Mat dilate = threshold.Dilate(kernel); //膨胀

            Cv2.FindContours(dilate, out var contours, out _, RetrievalModes.External,
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

            return null;
        }
    }
}