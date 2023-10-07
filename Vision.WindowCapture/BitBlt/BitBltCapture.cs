using System.Diagnostics;
using System.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using static Windows.Win32.PInvoke;

namespace Vision.WindowCapture.BitBlt
{
    public class BitBltCapture : IWindowCapture
    {
        private HWND _hWnd;

        public bool IsCapturing { get; private set; }

        public Task StartAsync(HWND hWnd)
        {
            _hWnd = hWnd;
            IsCapturing = true;
            return Task.CompletedTask;
        }

        public Bitmap? Capture()
        {
            if (_hWnd == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                GetWindowRect(_hWnd, out var windowRect);
                var width = windowRect.Width;
                var height = windowRect.Height;

                var hdcSrc = GetWindowDC(_hWnd);
                var hdcDest = CreateCompatibleDC(hdcSrc);
                var hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
                var hOld = SelectObject(hdcDest, hBitmap);
                Windows.Win32.PInvoke.BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, ROP_CODE.SRCCOPY);
                SelectObject(hdcDest, hOld);
                DeleteDC(hdcDest);
                ReleaseDC(_hWnd, hdcSrc);
                
                var bitmap = Image.FromHbitmap(hBitmap);
                DeleteObject(hBitmap);
                return bitmap;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null;
            }
        }

        public Task StopAsync()
        {
            _hWnd = HWND.Null;
            IsCapturing = false;
            return Task.CompletedTask;
        }
    }
}