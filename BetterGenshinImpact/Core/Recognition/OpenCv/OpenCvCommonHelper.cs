using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Recognition.OpenCv
{
    public class OpenCvCommonHelper
    {
        /// <summary>
        /// 计算灰度图中某个颜色的像素个数
        /// 快速遍历方法来自于: https://blog.csdn.net/TyroneKing/article/details/129108838
        /// </summary>
        /// <param name="mat"></param>
        /// <param name="color"></param>
        /// <returns></returns>
        public static int CountGrayMatColor(Mat mat, byte color)
        {
            Debug.Assert(mat.Depth() == MatType.CV_8U);
            var channels = mat.Channels();
            var nRows = mat.Rows;
            var nCols = mat.Cols * channels;
            if (mat.IsContinuous())
            {
                nCols *= nRows;
                nRows = 1;
            }

            var sum = 0;
            unsafe
            {
                for (var i = 0; i < nRows; i++)
                {
                    var p = mat.Ptr(i);
                    var b = (byte*)p.ToPointer();
                    for (var j = 0; j < nCols; j++)
                    {
                        if (b[j] == color)
                        {
                            sum++;
                        }
                    }
                }

            }
            return sum;
        }

        public static Mat Threshold(Mat src, Scalar low, Scalar high)
        {
            using var mask = new Mat();
            using var rgbMat = new Mat();

            Cv2.CvtColor(src, rgbMat, ColorConversionCodes.BGR2RGB);
            Cv2.InRange(rgbMat, low, high, mask);
            Cv2.Threshold(mask, mask, 0, 255, ThresholdTypes.Binary); //二值化
            return mask.Clone();
        }

        public static Mat Threshold(Mat src, Scalar s)
        {
            return Threshold(src, s, s);
        }
    }
}