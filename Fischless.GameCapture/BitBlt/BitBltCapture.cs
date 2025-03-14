using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using Vanara.PInvoke;

namespace Fischless.GameCapture.BitBlt;

public class BitBltCapture : IGameCapture
{
    private nint _hWnd;

    private static readonly object LockObject = new object();


    public CaptureModes Mode => CaptureModes.BitBlt;

    public bool IsCapturing { get; private set; }

    public void Dispose() => Stop();

    public void Start(nint hWnd, Dictionary<string, object>? settings = null)
    {
        _hWnd = hWnd;
        IsCapturing = true;
        if (settings != null && settings.TryGetValue("autoFixWin11BitBlt", out var value))
        {
            if (value is true)
            {
                BitBltRegistryHelper.SetDirectXUserGlobalSettings();
            }
        }
    }

    public Mat? Capture()
    {
        if (_hWnd == IntPtr.Zero)
        {
            return null;
        }

        // 加锁
        lock (LockObject)
        {
            User32.SafeReleaseHDC hdcSrc = User32.SafeReleaseHDC.Null;
            Gdi32.SafeHDC hdcDest = Gdi32.SafeHDC.Null;
            Gdi32.SafeHBITMAP hBitmap = Gdi32.SafeHBITMAP.Null;
            try
            {
                User32.GetClientRect(_hWnd, out var windowRect);
                int x = 0, y = 0;
                var width = windowRect.right - windowRect.left;
                var height = windowRect.bottom - windowRect.top;

                hdcSrc = User32.GetDC(_hWnd == IntPtr.Zero ? User32.GetDesktopWindow() : _hWnd);
                hdcDest = Gdi32.CreateCompatibleDC(hdcSrc);

                var bmi = new Gdi32.BITMAPINFO
                {
                    bmiHeader = new Gdi32.BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<Gdi32.BITMAPINFOHEADER>(),
                        biWidth = width,
                        biHeight = -height, // Top-down image
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = 0, // BI_RGB
                        biSizeImage = 0
                    }
                };

                nint bits = 0;
                hBitmap = Gdi32.CreateDIBSection(hdcDest, bmi, Gdi32.DIBColorMode.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
                var oldBitmap = Gdi32.SelectObject(hdcDest, hBitmap);

                Gdi32.StretchBlt(hdcDest, 0, 0, width, height, hdcSrc, x, y, width, height, Gdi32.RasterOperationMode.SRCCOPY);

                var mat = new Mat(height, width, MatType.CV_8UC4, bits);
                Mat bgrMat = new Mat();
                Cv2.CvtColor(mat, bgrMat, ColorConversionCodes.BGRA2BGR);

                Gdi32.SelectObject(hdcDest, oldBitmap);
                return bgrMat;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null!;
            }
            finally
            {
                if (hBitmap != Gdi32.SafeHBITMAP.Null)
                {
                    Gdi32.DeleteObject(hBitmap);
                }

                if (hdcDest != Gdi32.SafeHDC.Null)
                {
                    Gdi32.DeleteDC(hdcDest);
                }

                if (_hWnd != IntPtr.Zero)
                {
                    User32.ReleaseDC(_hWnd, hdcSrc);
                }
            }
        }
    }

    public void Stop()
    {
        _hWnd = IntPtr.Zero;
        IsCapturing = false;
    }
}