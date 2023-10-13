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
        private static readonly ILogger<MatchTemplateHelper> _logger = App.GetLogger<MatchTemplateHelper>();

        /// <summary>
        /// 模板匹配
        /// </summary>
        /// <param name="srcMat"></param>
        /// <param name="dstMat"></param>
        /// <param name="matchMode"></param>
        /// <param name="maskMat"></param>
        /// <param name="threshold"></param>
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
    }
}