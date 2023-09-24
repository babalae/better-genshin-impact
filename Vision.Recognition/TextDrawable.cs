using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Vision.Recognition
{
    [Serializable]
    public class TextDrawable
    {
        public string Text { get; set; }
        public Point Point { get; set; }

        public TextDrawable(string text, Point point)
        {
            Text = text;
            Point = point;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            var other = (TextDrawable)obj;
            return Point.Equals(other.Point);
        }

        public override int GetHashCode()
        {
            return Point.GetHashCode();
        }

        public bool IsEmpty => Point is { X: 0, Y: 0 };
    }
}