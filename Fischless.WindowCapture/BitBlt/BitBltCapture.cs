using System.Diagnostics;
using Vanara.PInvoke;

namespace Fischless.WindowCapture.BitBlt;

public class BitBltCapture : IWindowCapture
{
    private nint _hWnd;
    public bool IsCapturing { get; private set; }
    public bool IsClientEnabled { get; set; } = false;
    public bool IsCursorCaptureEnabled { get; set; } = false;

    public void Dispose()
    {
        Stop();
    }

    public void Start(nint hWnd)
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
            if (IsClientEnabled)
            {
                _ = User32.GetWindowRect(_hWnd, out var windowRect);
                var width = windowRect.right - windowRect.left;
                var height = windowRect.bottom - windowRect.top;

                var hdcSrc = User32.GetWindowDC(_hWnd);
                var hdcDest = Gdi32.CreateCompatibleDC(hdcSrc);
                var hBitmap = Gdi32.CreateCompatibleBitmap(hdcSrc, width, height);
                var hOld = Gdi32.SelectObject(hdcDest, hBitmap);
                _ = Gdi32.BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, Gdi32.RasterOperationMode.SRCCOPY);
                _ = Gdi32.SelectObject(hdcDest, hOld);
                _ = Gdi32.DeleteDC(hdcDest);
                _ = User32.ReleaseDC(_hWnd, hdcSrc);

                var bitmap = hBitmap.ToBitmap();
                _ = Gdi32.DeleteObject(hBitmap);
                return bitmap;
            }
            else
            {
                _ = User32.GetClientRect(_hWnd, out var windowRect);
                int x = default, y = default;
                int width = windowRect.right - windowRect.left;
                int height = windowRect.bottom - windowRect.top;

                Bitmap bitmap = new(width, height);
                using System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap);
                nint hdcDest = g.GetHdc();
                Gdi32.SafeHDC hdcSrc = User32.GetDC(_hWnd == IntPtr.Zero ? User32.GetDesktopWindow() : _hWnd);
                _ = Gdi32.StretchBlt(hdcDest, 0, 0, width, height, hdcSrc, x, y, width, height, Gdi32.RasterOperationMode.SRCCOPY);
                g.ReleaseHdc();
                _ = Gdi32.DeleteDC(hdcDest);
                _ = Gdi32.DeleteDC(hdcSrc);
                return bitmap;
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
        return null!;
    }

    public Bitmap? Capture(int x, int y, int width, int height)
    {
        if (_hWnd == IntPtr.Zero)
        {
            return null;
        }

        if (IsClientEnabled)
        {
            throw new NotSupportedException();
        }

        try
        {
            Bitmap copied = new(width, height);
            using System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(copied);
            nint hdcDest = g.GetHdc();
            Gdi32.SafeHDC hdcSrc = User32.GetDC(_hWnd == IntPtr.Zero ? User32.GetDesktopWindow() : _hWnd);
            _ = Gdi32.StretchBlt(hdcDest, 0, 0, width, height, hdcSrc, x, y, width, height, Gdi32.RasterOperationMode.SRCCOPY);
            g.ReleaseHdc();
            _ = Gdi32.DeleteDC(hdcDest);
            _ = Gdi32.DeleteDC(hdcSrc);
            return copied;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
        return null;
    }

    public void Stop()
    {
        _hWnd = IntPtr.Zero;
        IsCapturing = false;
    }
}
