using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Vision.Recognition.Helper.OpenCv;

namespace Vision.Recognition
{
    [Serializable]
    public class RectDrawable
    {
        public string? Name { get; set; }
        public Rect Rect { get; }
        public Pen Pen { get; } = new(Color.Red, 2);

        public RectDrawable(Rect rect, Pen? pen = null, string? name = null)
        {
            Rect = rect;
            Name = name;

            if (pen != null)
            {
                Pen = pen;
            }
        }

        public RectDrawable(Rect rect, string? name)
        {
            Rect = rect;
            Name = name;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            var other = (RectDrawable)obj;
            return Rect.Equals(other.Rect);
        }

        public override int GetHashCode()
        {
            return Rect.GetHashCode();
        }

        public bool IsEmpty => Rect.IsEmpty;
    }


    public static class RectDrawableExtension
    {
        public static RectDrawable ToRectDrawable(this Rect rect, Pen? pen = null, string? name = null)
        {
            return new RectDrawable(rect, pen, name);
        }

        public static RectDrawable ToRectDrawable(this OpenCvSharp.Rect rect, Pen? pen = null, string? name = null)
        {
            return new RectDrawable(rect.ToWindowsRectangle(), pen, name);
        }

        public static RectDrawable ToRectDrawable(this Rect rect, string? name)
        {
            return new RectDrawable(rect, name);
        }
    }
}