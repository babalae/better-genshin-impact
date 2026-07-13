using OpenCvSharp;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Drawing.Color;

namespace BetterGenshinImpact.Helpers.Extensions
{
    public static class BitmapExtension
    {
        public static BitmapSource ToBitmapSource(this Bitmap bitmap)
        {
            return bitmap.ToBitmapSource(out _);
        }

        public static BitmapSource ToBitmapSource(this Bitmap bitmap, out bool bottomUp)
        {
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

            var stride = bitmapData.Stride;
            var buffer = bitmapData.Scan0;
            if (stride < 0)
            {
                bottomUp = true;
                stride = -stride;
                buffer -= stride * (bitmapData.Height - 1);
            }
            else
            {
                bottomUp = false;
            }

            var pixelFormat = bitmap.PixelFormat switch
            {
                System.Drawing.Imaging.PixelFormat.Format24bppRgb => PixelFormats.Bgr24,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb => PixelFormats.Bgra32,
                _ => throw new NotSupportedException($"Unsupported pixel format {bitmap.PixelFormat}")
            };

            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                bitmap.HorizontalResolution, bitmap.VerticalResolution,
                pixelFormat, null,
                buffer, stride * bitmapData.Height, stride);

            bitmap.UnlockBits(bitmapData);

            return bitmapSource;
        }

        public static BitmapImage ToBitmapImage(this Bitmap bitmap)
        {
            var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            var image = new BitmapImage();
            image.BeginInit();
            ms.Seek(0, SeekOrigin.Begin);
            image.StreamSource = ms;
            image.EndInit();
            return image;
        }

        public static Scalar ToScalar(this Color color)
        {
            return new Scalar(color.R, color.G, color.B);
        }
    }
}
