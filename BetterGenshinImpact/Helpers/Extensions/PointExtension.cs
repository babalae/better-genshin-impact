using BetterGenshinImpact.GameTask;
using OpenCvSharp;
using System;
using Windows.Win32.Foundation;
using BetterGenshinImpact.Core.Simulator;

namespace BetterGenshinImpact.Helpers.Extensions
{
    public static class PointExtension
    {
        public static Point ToDesktopPosition(this Point point)
        {
            if (TaskContext.Instance().GameHandle == IntPtr.Zero)
            {
                return point;
            }

            var rc = SystemControl.GetWindowRect((HWND)TaskContext.Instance().GameHandle);
            return new Point(rc.X + point.X, rc.Y + point.Y);
        }

        public static Point ToDesktopPosition65535(this Point point)
        {
            var p = point.ToDesktopPosition();
            return new Point(p.X * 65535 / PrimaryScreen.WorkingArea.Width,
                p.Y * 65535 / PrimaryScreen.WorkingArea.Height);
        }

        public static Point ToDesktopPositionOffset(this Point point, int offsetX, int offsetY)
        {
            if (TaskContext.Instance().GameHandle == IntPtr.Zero)
            {
                return point;
            }

            var rc = SystemControl.GetWindowRect((HWND)TaskContext.Instance().GameHandle);
            return new Point(rc.X + point.X + offsetX, rc.Y + point.Y + offsetY);
        }

        public static Point ToDesktopPositionOffset65535(this Point point, int offsetX, int offsetY)
        {
            var p = point.ToDesktopPositionOffset(offsetX, offsetY);
            return new Point(p.X * 65535 / PrimaryScreen.WorkingArea.Width,
                p.Y * 65535 / PrimaryScreen.WorkingArea.Height);
        }

        public static System.Windows.Rect CenterPointToRect(this Point point, Mat targetMat)
        {
            return new System.Windows.Rect(point.X - targetMat.Width / 2, point.Y - targetMat.Height / 2,
                targetMat.Width, targetMat.Height);
        }
    }
}