using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using OpenCvSharp;
using Vanara.PInvoke;
using Point = System.Drawing.Point;

namespace BetterGenshinImpact.Core.Recognition.OpenCv
{
    public static class CommonExtension
    {
        public static unsafe OpenCvSharp.Point ToCvPoint(this Point point)
        {
            return *(OpenCvSharp.Point*)&point;
        }

        public static unsafe Point ToDrawingPoint(this OpenCvSharp.Point point)
        {
            return *(Point*)&point;
        }

        public static System.Windows.Point ToWindowsPoint(this OpenCvSharp.Point point)
        {
            return new System.Windows.Point(point.X, point.Y);
        }

        public static unsafe OpenCvSharp.Rect ToCvRect(this Rectangle rectangle)
        {
            return *(OpenCvSharp.Rect*)&rectangle;
        }

        public static System.Windows.Rect ToWindowsRectangle(this OpenCvSharp.Rect rect)
        {
            return new System.Windows.Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        public static System.Windows.Rect ToWindowsRectangleOffset(this OpenCvSharp.Rect rect, int offsetX, int offsetY)
        {
            return new System.Windows.Rect(rect.X + offsetX, rect.Y + offsetY, rect.Width, rect.Height);
        }

        public static unsafe Rectangle ToDrawingRectangle(this OpenCvSharp.Rect rect)
        {
            return *(Rectangle*)&rect;
        }

        public static Point GetCenterPoint(this Rectangle rectangle)
        {
            if (rectangle.IsEmpty)
            {
                throw new ArgumentException("rectangle is empty");
            }

            return new Point(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);
        }


        public static OpenCvSharp.Point GetCenterPoint(this RECT rectangle)
        {
            if (rectangle.IsEmpty)
            {
                throw new ArgumentException("rectangle is empty");
            }

            return new OpenCvSharp.Point(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);
        }

        public static OpenCvSharp.Point GetCenterPoint(this Rect rectangle)
        {
            if (rectangle == Rect.Empty)
            {
                throw new ArgumentException("rectangle is empty");
            }

            return new OpenCvSharp.Point(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);
        }

        public static Rect Multiply(this Rect rect, double assetScale)
        {
            if (rect == Rect.Empty)
            {
                throw new ArgumentException("rect is empty");
            }

            return new Rect((int)(rect.X * assetScale), (int)(rect.Y * assetScale), (int)(rect.Width * assetScale), (int)(rect.Height * assetScale));
        }


        public static System.Windows.Media.Color ToWindowsColor(this Color color)
        {
            return System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
        }
    }
}