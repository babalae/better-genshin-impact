using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Point = OpenCvSharp.Point;

namespace BetterGenshinImpact.Core.Recognition.OpenCv
{
    public class MatchTemplateHelper
    {
        private static readonly ILogger<MatchTemplateHelper> _logger = App.GetLogger<MatchTemplateHelper>();

        /// <summary>
        /// 模板匹配
        /// TODO 算法不一样的的时候找点的方法也不一样
        /// </summary>
        /// <param name="srcMat">原图像</param>
        /// <param name="dstMat">模板</param>
        /// <param name="matchMode">匹配方式</param>
        /// <param name="maskMat">遮罩</param>
        /// <param name="threshold">阈值</param>
        /// <returns>左上角的标点</returns>
        public static Point MatchTemplate(Mat srcMat, Mat dstMat, TemplateMatchModes matchMode, Mat? maskMat = null, double threshold = 0.8)
        {
            try
            {
                using var result = new Mat();
                if (maskMat == null)
                {
                    Cv2.MatchTemplate(srcMat, dstMat, result, matchMode);
                }
                else
                {
                    Cv2.MatchTemplate(srcMat, dstMat, result, matchMode, maskMat);
                }

                Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var point);

                if (maxValue >= threshold)
                {
                    return point;
                }

                return new Point();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                return new Point();
            }
        }

        /// <summary>
        /// 模板匹配多个结果
        /// 不好用
        /// </summary>
        /// <param name="srcMat"></param>
        /// <param name="dstMat"></param>
        /// <param name="maskMat"></param>
        /// <param name="threshold"></param>
        /// <param name="maxCount"></param>
        /// <returns></returns>
        public static List<Point> MatchTemplateMulti(Mat srcMat, Mat dstMat, Mat? maskMat = null, double threshold = 0.8, int maxCount = 8)
        {
            var points = new List<Point>();
            try
            {
                using var result = new Mat();
                if (maskMat == null)
                {
                    Cv2.MatchTemplate(srcMat, dstMat, result, TemplateMatchModes.CCoeffNormed);
                }
                else
                {
                    Cv2.MatchTemplate(srcMat, dstMat, result, TemplateMatchModes.CCoeffNormed, maskMat);
                }

                //while (true)
                //{
                //    Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLoc);

                //    if (maxValue >= threshold)
                //    {
                //        points.Add(maxLoc);

                //        //Fill in the res Mat so you don't find the same area again in the MinMaxLoc
                //        Cv2.FloodFill(result, maxLoc, new Scalar(0), out _, new Scalar(0.1), new Scalar(1.0));
                //    }
                //    else
                //    {
                //        break;
                //    }
                //}


                var mask = new Mat(result.Height, result.Width, MatType.CV_8UC1, Scalar.White);
                var maskSub = new Mat(result.Height, result.Width, MatType.CV_8UC1, Scalar.Black);
                while (true)
                {
                    Cv2.MinMaxLoc(result, out _, out var maxValue, out _, out var maxLoc, mask);
                    var maskRect = new Rect(maxLoc.X, maxLoc.Y, dstMat.Width, dstMat.Height);
                    maskSub.Rectangle(maskRect, Scalar.White, -1);
                    mask -= maskSub;
                    if (maxValue >= threshold)
                    {
                        points.Add(maxLoc);
                    }
                    else
                    {
                        break;
                    }
                }

                return points;
            }
            catch (Exception ex)
            {
                _logger.LogError("{Ex}", ex);
                return points;
            }
        }

        public static List<Point> MatchTemplateMulti(Mat srcMat, Mat dstMat, double threshold)
        {
            return MatchTemplateMulti(srcMat, dstMat, null, threshold);
        }
    }
}