using System;
using System.Drawing;
using System.Windows;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.View.Drawable
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
            var scale = TaskContext.Instance().DpiScale;
            rect.X /= scale;
            rect.Y /= scale;
            rect.Width /= scale;
            rect.Height /= scale;
            return new RectDrawable(rect, pen, name);
        }

        public static RectDrawable ToRectDrawable(this OpenCvSharp.Rect rect, Pen? pen = null, string? name = null)
        {
            var scale = TaskContext.Instance().DpiScale;
            rect.X = (int)(rect.X / scale);
            rect.Y = (int)(rect.Y / scale);
            rect.Width = (int)(rect.Width / scale);
            rect.Height = (int)(rect.Height / scale);
            return new RectDrawable(rect.ToWindowsRectangle(), pen, name);
        }
    }
}