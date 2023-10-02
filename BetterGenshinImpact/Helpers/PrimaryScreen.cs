using System.Drawing;
using Windows.Win32.Graphics.Gdi;
using static Windows.Win32.PInvoke;

namespace BetterGenshinImpact.Helpers
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
                var hdc = GetDC(default);
                var size = new Size
                {
                    Width = GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.HORZRES),
                    Height = GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.VERTRES)
                };
                ReleaseDC(default, hdc);
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
                var hdc = GetDC(default);
                var dpiX = GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.LOGPIXELSX);
                ReleaseDC(default, hdc);
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
                var hdc = GetDC(default);
                var dpiX = GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.LOGPIXELSY);
                ReleaseDC(default, hdc);
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
                var hdc = GetDC(default);
                var size = new Size
                {
                    Width = GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.DESKTOPHORZRES),
                    Height = GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.DESKTOPVERTRES)
                };
                ReleaseDC(default, hdc);
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
                var hdc = GetDC(default);
                var scaleX = (float)GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.DESKTOPHORZRES) / GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.HORZRES);
                ReleaseDC(default, hdc);
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
                var hdc = GetDC(default);
                var scaleY = (float)GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.DESKTOPVERTRES) / GetDeviceCaps(hdc, GET_DEVICE_CAPS_INDEX.VERTRES);
                ReleaseDC(default, hdc);
                return scaleY;
            }
        }
    }
}
