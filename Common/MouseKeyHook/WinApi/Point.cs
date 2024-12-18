// This code is distributed under MIT license. 
// Copyright (c) 2015 George Mamaladze
// See license.txt or https://mit-license.org/

using System.Runtime.InteropServices;

namespace Gma.System.MouseKeyHook.WinApi
{
    /// <summary>
    ///     The Point structure defines the X- and Y- coordinates of a point.
    /// </summary>
    /// <remarks>
    ///     http://msdn.microsoft.com/library/default.asp?url=/library/en-us/gdi/rectangl_0tiq.asp
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        /// <summary>
        ///     Specifies the X-coordinate of the point.
        /// </summary>
        public int X;

        /// <summary>
        ///     Specifies the Y-coordinate of the point.
        /// </summary>
        public int Y;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static bool operator ==(Point a, Point b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        public static bool operator !=(Point a, Point b)
        {
            return !(a == b);
        }

        public bool Equals(Point other)
        {
            return other.X == X && other.Y == Y;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (obj.GetType() != typeof(Point)) return false;
            return Equals((Point) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }
    }
}