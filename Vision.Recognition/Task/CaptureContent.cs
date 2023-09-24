using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;
using Vision.Recognition.Helper.OpenCv;

namespace Vision.Recognition.Task
{
    /// <summary>
    /// 捕获的内容
    /// 以及一些多个trigger会用到的内容
    /// </summary>
    public class CaptureContent
    {
        public Bitmap SrcBitmap { get; }
        public int FrameIndex { get; private set; }

        public CaptureContent(Bitmap srcBitmap, int frameIndex)
        {
            SrcBitmap = srcBitmap;
            FrameIndex = frameIndex;
        }

        private Mat? _srcMat;
        public Mat SrcMat
        {
            get
            {
                _srcMat ??= SrcBitmap.ToMat();
                return _srcMat;
            }
        }

        private Mat? _srcGreyMat;
        public Mat SrcGreyMat
        {
            get
            {
                _srcGreyMat ??= new Mat();
                Cv2.CvtColor(SrcMat, _srcGreyMat, ColorConversionCodes.BGR2GRAY);
                return _srcGreyMat;
            }
        }
    }
}