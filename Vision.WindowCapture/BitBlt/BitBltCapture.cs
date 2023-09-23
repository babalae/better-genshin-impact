using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Vanara.Extensions;
using Vanara.PInvoke;

namespace Vision.WindowCapture.BitBlt
{
    public class BitBltCapture : IWindowCapture
    {
        private IntPtr _hWnd;
        public bool IsCapturing { get; private set; }

        public void Start(IntPtr hWnd)
        {
            _hWnd = hWnd;
            IsCapturing = true;
        }

        public Bitmap? Capture()
        {
            if (_hWnd == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                User32.GetWindowRect(_hWnd, out var windowRect);
                var width = windowRect.Width;
                var height = windowRect.Height;

                var hdcSrc = User32.GetWindowDC(_hWnd);
                var hdcDest = Gdi32.CreateCompatibleDC(hdcSrc);
                var hBitmap = Gdi32.CreateCompatibleBitmap(hdcSrc, width, height);
                var hOld = Gdi32.SelectObject(hdcDest, hBitmap);
                Gdi32.BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, Gdi32.RasterOperationMode.SRCCOPY);
                Gdi32.SelectObject(hdcDest, hOld);
                Gdi32.DeleteDC(hdcDest);
                User32.ReleaseDC(_hWnd, hdcSrc);

                var bitmap = hBitmap.ToBitmap();
                Gdi32.DeleteObject(hBitmap);
                return bitmap;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null;
            }
        }

        public void Stop()
        {
            _hWnd = IntPtr.Zero;
            IsCapturing = false;
        }
    }
}