using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace Fischless.GameCapture;

/// <summary>
/// 捕获的图像
/// </summary>
public class CaptureImageRes : IDisposable
{
    public Bitmap? Bitmap { get; private set; }
    public Mat? Mat { get; private set; }

    public int Width => Mat?.Width ?? Bitmap?.Width ?? 0;
    public int Height => Mat?.Height ?? Bitmap?.Height ?? 0;

    private CaptureImageRes(Mat mat)
    {
        Mat = mat;
    }

    private CaptureImageRes(Bitmap bitmap)
    {
        Bitmap = bitmap;
    }

    public static CaptureImageRes? BuildNullable(Bitmap? bitmap)
    {
        if (bitmap == null)
        {
            return null;
        }
        return new CaptureImageRes(bitmap);
    }

    public static CaptureImageRes? BuildNullable(Mat? mat)
    {
        if (mat == null)
        {
            return null;
        }
        return new CaptureImageRes(mat);
    }

    /// <summary>
    /// 非特殊情况不要使用这个方法，会造成额外的性能消耗
    /// </summary>
    /// <returns></returns>
    public Mat? ForceGetMat()
    {
        if (Mat == null)
        {
            Mat = Bitmap?.ToMat();
        }
        return Mat;
    }
    
    /// <summary>
    /// 非特殊情况不要使用这个方法，会造成额外的性能消耗
    /// </summary>
    /// <returns></returns>
    public Bitmap? ForceGetBitmap()
    {
        if (Bitmap == null)
        {
            Bitmap = Mat?.ToBitmap();
        }
        return Bitmap;
    }

    public void Dispose()
    {
        Bitmap?.Dispose();
        Mat?.Dispose();
        GC.SuppressFinalize(this);
    }
}
