using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using OpenCvSharp;
using Vision.Recognition.Helper.Simulator;

namespace BetterGenshinImpact.Utils.Extensions
{
    public static class PointExtension
    {
        public static Point ToDesktopPosition(this Point point)
        {
            if (TaskContext.Instance().GameHandle == IntPtr.Zero)
            {
                return point;
            }

            var rc = SystemControl.GetWindowRect(TaskContext.Instance().GameHandle);
            return new Point(rc.X + point.X, rc.Y + point.Y);
        }

        public static Point ToDesktopPosition65535(this Point point)
        {
            var p = point.ToDesktopPosition();
            return new Point(p.X * 65535 / PrimaryScreen.WorkingArea.Width, p.Y * 65535 / PrimaryScreen.WorkingArea.Height);
        }

        public static Point ToDesktopPositionOffset(this Point point, int offsetX, int offsetY)
        {
            if (TaskContext.Instance().GameHandle == IntPtr.Zero) 
            {
                return point;
            } 

            var rc = SystemControl.GetWindowRect(TaskContext.Instance().GameHandle);
            return new Point(rc.X + point.X + offsetX, rc.Y + point.Y + offsetY);
        }

        public static Point ToDesktopPositionOffset65535(this Point point, int offsetX, int offsetY)
        {
            var p = point.ToDesktopPositionOffset(offsetX, offsetY);
            return new Point(p.X * 65535 / PrimaryScreen.WorkingArea.Width, p.Y * 65535 / PrimaryScreen.WorkingArea.Height);
        }
    }
}