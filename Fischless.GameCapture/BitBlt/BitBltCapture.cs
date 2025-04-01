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
                if (!User32.GetClientRect(_hWnd, out var windowRect))
                {
                    Debug.Fail("Failed to get client rectangle");
                    return null;
                }

                var width = windowRect.right - windowRect.left;
                var height = windowRect.bottom - windowRect.top;

                hdcSrc = User32.GetDC(_hWnd == IntPtr.Zero ? User32.GetDesktopWindow() : _hWnd);
                if (hdcSrc.IsInvalid)
                {
                    Debug.WriteLine($"Failed to get DC for {_hWnd}");
                    return null;
                }

                hdcDest = Gdi32.CreateCompatibleDC(hdcSrc);
                if (hdcSrc.IsInvalid)
                {
                    Debug.Fail("Failed to create CompatibleDC");
                    return null;
                }

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

                hBitmap = Gdi32.CreateDIBSection(hdcDest, bmi, Gdi32.DIBColorMode.DIB_RGB_COLORS, out var bits,
                    IntPtr.Zero,
                    0);
                if (hBitmap.IsInvalid || bits == 0)
                {
                    Debug.WriteLine($"Failed to create dIB section for {_hWnd}");
                    return null;
                }

                var oldBitmap = Gdi32.SelectObject(hdcDest, hBitmap);
                if (oldBitmap.IsNull)
                {
                    return null;
                }

                if (!Gdi32.BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, Gdi32.RasterOperationMode.SRCCOPY))
                {
                    Debug.WriteLine($"BitBlt failed for {_hWnd}");
                    return null;
                }

                using var mat = Mat.FromPixelData(height, width, MatType.CV_8UC4, bits);
                Gdi32.SelectObject(hdcDest, oldBitmap);
                if (!mat.Empty())
                {
                    var bgrMat = new Mat();
                    Cv2.CvtColor(mat, bgrMat, ColorConversionCodes.BGRA2BGR);
                    return bgrMat;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return null!;
            }
            finally
            {
                if (!hBitmap.IsNull)
                {
                    Gdi32.DeleteObject(hBitmap);
                }

                if (!hdcDest.IsNull)
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