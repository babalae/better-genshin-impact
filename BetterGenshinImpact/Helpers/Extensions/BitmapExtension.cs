using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace BetterGenshinImpact.Helpers.Extensions
{
    public static class BitmapExtension
    {

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
