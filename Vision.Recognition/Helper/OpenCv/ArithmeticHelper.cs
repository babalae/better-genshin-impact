using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vision.Recognition.Helper.OpenCv
{
    public class ArithmeticHelper
    {
        /// <summary>
        /// 水平投影
        /// </summary>
        /// <param name="gray"></param>
        /// <returns></returns>
        public static int[] HorizontalProjection(Mat gray)
        {
            var projection = new int[gray.Height];
            //对每一行计算投影值
            for (var y = 0; y < gray.Height; ++y)
            {
                //遍历这一行的每一个像素，如果是有效的，累加投影值
                for (var x = 0; x < gray.Width; ++x)
                {
                    var s = gray.Get<Vec2b>(y, x);
                    if (s.Item0 == 255)
                    {
                        projection[y]++;
                    }
                }
            }

            return projection;
        }

        /// <summary>
        /// 垂直投影
        /// </summary>
        /// <param name="gray"></param>
        /// <returns></returns>
        public static int[] VerticalProjection(Mat gray)
        {
            var projection = new int[gray.Width];
            //遍历每一列计算投影值
            for (var x = 0; x < gray.Width; ++x)
            {
                for (var y = 0; y < gray.Height; ++y)
                {
                    var s = gray.Get<Vec2b>(y, x);
                    if (s.Item0 == 255)
                    {
                        projection[x]++;
                    }
                }
            }

            return projection;
        }
    }
}
