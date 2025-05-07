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
    public CaptureSession? Session { get; }

    public int Width => Mat?.Width ?? Bitmap?.Width ?? 0;
    public int Height => Mat?.Height ?? Bitmap?.Height ?? 0;

    private CaptureImageRes(Mat mat, CaptureSession? session = null)
    {
        Mat = mat;
        Session = session;
    }

    private CaptureImageRes(Bitmap bitmap, CaptureSession? session = null)
    {
        Bitmap = bitmap;
        Session = session;
    }

    public static CaptureImageRes? BuildNullable(Bitmap? bitmap, CaptureSession? session = null)
    {
        if (bitmap == null)
        {
            return null;
        }
        return new CaptureImageRes(bitmap, session);
    }

    public static CaptureImageRes? BuildNullable(Mat? mat, CaptureSession? session = null)
    {
        if (mat == null)
        {
            return null;
        }
        return new CaptureImageRes(mat, session);
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
