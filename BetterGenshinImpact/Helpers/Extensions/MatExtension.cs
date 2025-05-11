using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace BetterGenshinImpact.Helpers.Extensions;

public static class MatExtension
{
    public static WriteableBitmap ToWriteableBitmap(this Mat mat)
    {
        PixelFormat pixelFormat;
        var type = mat.Type();
        if (type == MatType.CV_8UC3)
        {
            pixelFormat = PixelFormats.Bgr24;
        }
        else if (type == MatType.CV_8UC4)
        {
            pixelFormat = PixelFormats.Bgra32;
        }
        else
        {
            throw new NotSupportedException($"Unsupported pixel format {type}");
        }

        var bitmap = new WriteableBitmap(mat.Width, mat.Height, 96, 96, pixelFormat, null);
        mat.UpdateWriteableBitmap(bitmap);

        return bitmap;
    }

    public static unsafe void UpdateWriteableBitmap(this Mat mat, WriteableBitmap bitmap)
    {
        bitmap.Lock();
        var stride = bitmap.BackBufferStride;
        var step = mat.Step();
        if (stride == step)
        {
            var length = stride * bitmap.PixelHeight;
            Buffer.MemoryCopy(mat.Data.ToPointer(), bitmap.BackBuffer.ToPointer(), length, length);
        }
        else
        {
            var length = Math.Min(stride, step);
            for (var i = 0; i < bitmap.PixelHeight; i++)
            {
                Buffer.MemoryCopy((void*)(mat.Data + i * step), (void*)(bitmap.BackBuffer + i * stride), length, length);
            }
        }
        bitmap.AddDirtyRect(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Unlock();
    }
}
