using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.Gdi32;

namespace Vision.Recognition.Helper.Simulator
{
    public class PrimaryScreen
    {
        /// <summary>
        /// 获取屏幕分辨率当前物理大小
        /// </summary>
        public static Size WorkingArea
        {
            get
            {
                var hdc = User32.GetDC(IntPtr.Zero);
                var size = new Size
                {
                    Width = Gdi32.GetDeviceCaps(hdc, DeviceCap.HORZRES),
                    Height = Gdi32.GetDeviceCaps(hdc, DeviceCap.VERTRES)
                };
                User32.ReleaseDC(IntPtr.Zero, hdc);
                return size;
            }
        }
        /// <summary>
        /// 当前系统DPI_X 大小 一般为96
        /// </summary>
        public static int DpiX
        {
            get
            {
                var hdc = User32.GetDC(IntPtr.Zero);
                var dpiX = Gdi32.GetDeviceCaps(hdc, DeviceCap.LOGPIXELSX);
                User32.ReleaseDC(IntPtr.Zero, hdc);
                return dpiX;
            }
        }
        /// <summary>
        /// 当前系统DPI_Y 大小 一般为96
        /// </summary>
        public static int DpiY
        {
            get
            {
                var hdc = User32.GetDC(IntPtr.Zero);
                var dpiX = Gdi32.GetDeviceCaps(hdc, DeviceCap.LOGPIXELSY);
                User32.ReleaseDC(IntPtr.Zero, hdc);
                return dpiX;
            }
        }
        /// <summary>
        /// 获取真实设置的桌面分辨率大小
        /// </summary>
        public static Size DESKTOP
        {
            get
            {
                var hdc = User32.GetDC(IntPtr.Zero);
                var size = new Size
                {
                    Width = Gdi32.GetDeviceCaps(hdc, DeviceCap.DESKTOPHORZRES),
                    Height = Gdi32.GetDeviceCaps(hdc, DeviceCap.DESKTOPVERTRES)
                };
                User32.ReleaseDC(IntPtr.Zero, hdc);
                return size;
            }
        }

        /// <summary>
        /// 获取宽度缩放百分比
        /// </summary>
        public static float ScaleX
        {
            get
            {
                var hdc = User32.GetDC(IntPtr.Zero);
                var scaleX = (float)Gdi32.GetDeviceCaps(hdc, DeviceCap.DESKTOPHORZRES) / (float)Gdi32.GetDeviceCaps(hdc, DeviceCap.HORZRES);
                User32.ReleaseDC(IntPtr.Zero, hdc);
                return scaleX;
            }
        }
        /// <summary>
        /// 获取高度缩放百分比
        /// </summary>
        public static float ScaleY
        {
            get
            {
                var hdc = User32.GetDC(IntPtr.Zero);
                var scaleY = (float)Gdi32.GetDeviceCaps(hdc, DeviceCap.DESKTOPVERTRES) / (float)Gdi32.GetDeviceCaps(hdc, DeviceCap.VERTRES);
                User32.ReleaseDC(IntPtr.Zero, hdc);
                return scaleY;
            }
        }
    }
}
