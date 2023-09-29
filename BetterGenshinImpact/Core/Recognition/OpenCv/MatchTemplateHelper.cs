using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Point = OpenCvSharp.Point;
using BetterGenshinImpact.GameTask.AutoSkip;

namespace BetterGenshinImpact.Core.Recognition.OpenCv
{
    public class MatchTemplateHelper
    {
        private static ILogger<MatchTemplateHelper> _logger = App.GetLogger<MatchTemplateHelper>();

        public static double WidthScale = 1;
        public static double HeightScale = 1;


        public static Point FindSingleTarget(Bitmap imgSrc, Bitmap imgSub, double threshold = 0.8)
        {
            Mat? srcMat = null;
            Mat? dstMat = null;
            try
            {
                srcMat = imgSrc.ToMat();
                dstMat = imgSub.ToMat();
                return FindSingleTarget(srcMat, dstMat, threshold);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                return new Point();
            }
            finally
            {
                srcMat?.Dispose();
                dstMat?.Dispose();
            }
        }

        public static Point FindSingleTarget(Mat srcMat, Mat dstMat, double threshold = 0.8)
        {
            Point p = new Point();

            OutputArray? outArray = null;
            try
            {
                dstMat = ResizeHelper.Resize(dstMat, WidthScale);

                outArray = OutputArray.Create(srcMat);
                Cv2.MatchTemplate(srcMat, dstMat, outArray, TemplateMatchModes.CCoeffNormed);
                double minValue, maxValue;
                Point location, point;
                Cv2.MinMaxLoc(InputArray.Create(outArray.GetMat()), out minValue, out maxValue,
                    out location, out point);

                if (maxValue >= threshold)
                {
                    p = new Point(point.X + dstMat.Width / 2, point.Y + dstMat.Height / 2);
                    //if (VisionContext.Instance().Drawable)
                    //{
                        //VisionContext.Instance().DrawContent.PutRect("", new System.Windows.Rect(point.X, point.Y, dstMat.Width, dstMat.Height));
                        //VisionContext.Instance().DrawContent.TextList
                        //    .Add(new Tuple<System.Windows.Point, string>(new System.Windows.Point(point.X, point.Y - 10), maxValue.ToString("0.00")));
                    //}
                }

                return p;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                return p;
            }
            finally
            {
                outArray?.Dispose();
            }
        }

        public static List<Point> FindMultiTarget(Mat srcMat, Mat dstMat, string title, out Mat resMat,
            double threshold = 0.8, int findTargetCount = 8)
        {
            List<Point> pointList = new List<Point>();
            resMat = srcMat.Clone();
            try
            {
                dstMat = ResizeHelper.Resize(dstMat, WidthScale);

                Mat matchResult = new Mat();
                Cv2.MatchTemplate(srcMat, dstMat, matchResult, TemplateMatchModes.CCoeffNormed);

                double minValue = 0;
                double maxValue = 0;
                Point minLoc = new();

                //寻找最几个最值的位置
                Mat mask = new Mat(matchResult.Height, matchResult.Width, MatType.CV_8UC1, Scalar.White);
                Mat maskSub = new Mat(matchResult.Height, matchResult.Width, MatType.CV_8UC1, Scalar.Black);
                var point = new OpenCvSharp.Point(0, 0);
                for (int i = 0; i < findTargetCount; i++)
                {
                    Cv2.MinMaxLoc(matchResult, out minValue, out maxValue, out minLoc, out point, mask);
                    Rect maskRect = new Rect(point.X - dstMat.Width / 2, point.Y - dstMat.Height / 2, dstMat.Width,
                        dstMat.Height);
                    maskSub.Rectangle(maskRect, Scalar.White, -1);
                    mask -= maskSub;
                    if (maxValue >= threshold)
                    {
                        pointList.Add(new Point(point.X + dstMat.Width / 2, point.Y + dstMat.Height / 2));

                        //if (VisionContext.Instance().Drawable)
                        //{
                        //    VisionContext.Instance().DrawContent.RectList
                        //        .Add(new System.Windows.Rect(point.X, point.Y, dstMat.Width, dstMat.Height));
                        //    VisionContext.Instance().DrawContent.TextList
                        //        .Add(new Tuple<System.Windows.Point, string>(new System.Windows.Point(point.X, point.Y - 10), maxValue.ToString("0.00")));
                        //}
                        //if (IsDebug)
                        //{
                        //    VisionContext.Instance().Log
                        //        ?.LogInformation(title + " " + maxValue.ToString("0.000") + " " + point);
                        //    Cv2.Rectangle(resMat, point,
                        //        new OpenCvSharp.Point(point.X + dstMat.Width, point.Y + dstMat.Height),
                        //        Scalar.Red, 2);
                        //    Cv2.PutText(resMat, title + " " + maxValue.ToString("0.00"),
                        //        new OpenCvSharp.Point(point.X, point.Y - 10),
                        //        HersheyFonts.HersheySimplex, 0.5, Scalar.Red);
                        //}
                    }
                    else
                    {
                        break;
                    }
                }

                return pointList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.ToString());
                return pointList;
            }
            finally
            {
                srcMat?.Dispose();
                dstMat?.Dispose();
            }
        }


        public static Dictionary<string, List<Point>> FindMultiPicFromOneImage(Bitmap imgSrc,
            Dictionary<string, Bitmap> imgSubDictionary, double threshold = 0.8)
        {
            Dictionary<string, List<Point>> dictionary = new Dictionary<string, List<Point>>();
            Mat srcMat = imgSrc.ToMat();
            Mat resMat;

            foreach (KeyValuePair<string, Bitmap> kvp in imgSubDictionary)
            {
                dictionary.Add(kvp.Key, FindMultiTarget(srcMat, kvp.Value.ToMat(), kvp.Key, out resMat, threshold));
                srcMat = resMat.Clone();
            }

            return dictionary;
        }

        public static Dictionary<string, List<Point>> FindMultiPicFromOneImage(Mat srcMat,
            Dictionary<string, Bitmap> imgSubDictionary, double threshold = 0.8)
        {
            Dictionary<string, List<Point>> dictionary = new Dictionary<string, List<Point>>();
            Mat resMat;
            foreach (KeyValuePair<string, Bitmap> kvp in imgSubDictionary)
            {
                dictionary.Add(kvp.Key, FindMultiTarget(srcMat, kvp.Value.ToMat(), kvp.Key, out resMat, threshold));
                srcMat = resMat.Clone();
            }

            return dictionary;
        }
    }
}