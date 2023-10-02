namespace Fischless.WindowCapture;

internal static class BitmapExtensions
{
    public static Bitmap Crop(this Bitmap src, int x, int y, int width, int height)
    {
        Rectangle cropRect = new(x, y, width, height);
        Bitmap target = new(cropRect.Width, cropRect.Height);

        using System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(target);
        g.DrawImage(src, new Rectangle(0, 0, target.Width, target.Height), cropRect, GraphicsUnit.Pixel);
        return target;
    }
}
