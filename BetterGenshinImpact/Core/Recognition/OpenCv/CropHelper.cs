using System;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OpenCv;

/// <summary>
///     图片剪裁
/// </summary>
[Obsolete]
public class CropHelper
{
    public static Mat CutRightTop(Mat srcMat, int saveRightWidth, int saveTopHeight)
    {
        srcMat = new Mat(srcMat, new Rect(srcMat.Width - saveRightWidth, 0, saveRightWidth, saveTopHeight));
        return srcMat;
    }

    public static Mat CutRightBottom(Mat srcMat, int saveRightWidth, int saveBottomHeight)
    {
        srcMat = new Mat(srcMat, new Rect(srcMat.Width - saveRightWidth, srcMat.Height - saveBottomHeight, saveRightWidth, saveBottomHeight));
        return srcMat;
    }

    public static Mat CutLeftTop(Mat srcMat, int saveLeftWidth, int saveTopHeight)
    {
        srcMat = new Mat(srcMat, new Rect(0, 0, saveLeftWidth, saveTopHeight));
        return srcMat;
    }

    public static Mat CutLeftBottom(Mat srcMat, int saveLeftWidth, int saveBottomHeight)
    {
        srcMat = new Mat(srcMat, new Rect(0, srcMat.Height - saveBottomHeight, saveLeftWidth, saveBottomHeight));
        return srcMat;
    }

    public static Mat CutTop(Mat srcMat, int saveTopHeight)
    {
        srcMat = new Mat(srcMat, new Rect(0, 0, srcMat.Width, saveTopHeight));
        return srcMat;
    }

    public static Mat CutBottom(Mat srcMat, int saveBottomHeight)
    {
        srcMat = new Mat(srcMat, new Rect(0, srcMat.Height - saveBottomHeight, srcMat.Width, saveBottomHeight));
        return srcMat;
    }

    public static Mat CutRight(Mat srcMat, int saveRightWidth)
    {
        srcMat = new Mat(srcMat, new Rect(srcMat.Width - saveRightWidth, 0, saveRightWidth, srcMat.Height));
        return srcMat;
    }

    public static Mat CutLeft(Mat srcMat, int saveLeftWidth)
    {
        srcMat = new Mat(srcMat, new Rect(0, 0, saveLeftWidth, srcMat.Height));
        return srcMat;
    }
}
