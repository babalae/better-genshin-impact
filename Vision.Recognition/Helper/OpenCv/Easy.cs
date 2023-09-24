using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using OpenCvSharp;

namespace Vision.Recognition.Helper.OpenCv
{
    /// <summary>
    /// 参考 Airtest 语法写的极致简单的识别与操作类
    /// https://airtest-refactor.doc.io.netease.com/airtest-project-docs/IDEdocs/airtest_framework/3_airtest_image/
    /// </summary>
    public class Easy
    {
        public Mat SrcMat { get; set; }

        public Easy(Mat srcMat)
        {
            this.SrcMat = srcMat;
        }

        public Point Click(Mat targetMat, double threshold = 0.8, int intervalMillisecond = 300)
        {
            Point p = MatchTemplateHelper.FindSingleTarget(SrcMat, targetMat, threshold);
            if (p.X > 0 && p.Y > 0)
            {
                VisionContext.Instance().DrawContent.PutRect("ClickMatch", new System.Windows.Rect(
                    p.X - targetMat.Width * 1.0 / 2, p.Y - targetMat.Height * 1.0 / 2, targetMat.Width,
                    targetMat.Height));
                // click...
            }

            return p;
        }

        public Point Exist(Mat targetMat, double threshold = 0.8)
        {
            return MatchTemplateHelper.FindSingleTarget(SrcMat, targetMat, threshold);
        }
    }
}