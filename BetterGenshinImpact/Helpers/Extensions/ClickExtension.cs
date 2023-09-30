using BetterGenshinImpact.Core.Simulator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;
using WindowsInput;

namespace BetterGenshinImpact.Helpers.Extensions
{
    public static class ClickExtension
    {
        public static void Click(this Point point)
        {
            new InputSimulator().Mouse.MoveMouseTo(point.X * 65535 * 1d / PrimaryScreen.WorkingArea.Width,
                point.Y * 65535 * 1d / PrimaryScreen.WorkingArea.Height).LeftButtonClick();
        }

        public static void ClickCenter(this Rect rect)
        {
            new InputSimulator().Mouse.MoveMouseTo((rect.X + rect.Width * 1d / 2) * 65535 / PrimaryScreen.WorkingArea.Width,
                (rect.Y + rect.Height * 1d / 2) * 65535 / PrimaryScreen.WorkingArea.Height).LeftButtonClick();
        }
    }
}