using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;

namespace Vision.Recognition.Helper.OpenCv
{
    public static class CommonExtension
    {
        public static OpenCvSharp.Point ToCvPoint(this Point point)
        {
            return new OpenCvSharp.Point(point.X, point.Y);
        }

        public static Point ToDrawingPoint(this OpenCvSharp.Point point)
        {
            return new Point(point.X, point.Y);
        }

        public static System.Windows.Point ToWindowsPoint(this OpenCvSharp.Point point)
        {
            return new System.Windows.Point(point.X, point.Y);
        }

        public static OpenCvSharp.Rect ToCvRect(this Rectangle rectangle)
        {
            return new OpenCvSharp.Rect(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        }

        public static System.Windows.Rect ToWindowsRectangle(this OpenCvSharp.Rect rect)
        {
            return new System.Windows.Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        public static System.Windows.Rect ToWindowsRectangleOffset(this OpenCvSharp.Rect rect, int offsetX, int offsetY)
        {
            return new System.Windows.Rect(rect.X + offsetX, rect.Y + offsetY, rect.Width, rect.Height);
        }

        public static Rectangle ToDrawingRectangle(this OpenCvSharp.Rect rect)
        {
            return new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }

        public static Point GetCenterPoint(this Rectangle rectangle)
        {
            if (rectangle.IsEmpty)
            {
                throw new ArgumentException("rectangle is empty");
            }
            return new Point(rectangle.X + rectangle.Width / 2, rectangle.Y + rectangle.Height / 2);
        }
    }
}
