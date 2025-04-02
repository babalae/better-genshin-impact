using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using Vanara.PInvoke;

namespace Fischless.GameCapture.BitBlt;

public class BitBltSession : IDisposable
{
    // 窗口句柄
    private readonly HWND _hWnd;

    // 来源DC
    private User32.SafeReleaseHDC _hdcSrc;

    // 缓冲区 CompatibleDC
    private Gdi32.SafeHDC _hdcDest;

    // 位图句柄
    private Gdi32.SafeHBITMAP _hBitmap;

    // 位图指针，这个指针会在位图释放时自动释放
    private IntPtr _bitsPtr;

    // 旧位图，析构时一起释放掉
    private HGDIOBJ _oldBitmap;
    public int Width { get; }
    public int Height { get; }

    private static readonly object LockObject = new object();


    public BitBltSession(HWND hWnd, int w, int h)
    {
        lock (LockObject)
        {
            try
            {
                if (hWnd.IsNull)
                {
                    throw new Exception("hWnd is invalid");
                }

                _hWnd = hWnd;
                if (w <= 0 || h <= 0)
                {
                    throw new Exception("Invalid width or height");
                }

                Width = w;
                Height = h;
                _hdcSrc = User32.GetDC(_hWnd);
                if (_hdcSrc.IsInvalid)
                {
                    throw new Exception("Failed to get DC for {_hWnd}");
                }

                _hdcDest = Gdi32.CreateCompatibleDC(_hdcSrc);
                if (_hdcSrc.IsInvalid)
                {
                    Debug.Fail("Failed to create CompatibleDC");
                    throw new Exception("Failed to create CompatibleDC for {_hWnd}");
                }


                var bmi = new Gdi32.BITMAPINFO
                {
                    bmiHeader = new Gdi32.BITMAPINFOHEADER
                    {
                        biSize = (uint)Marshal.SizeOf<Gdi32.BITMAPINFOHEADER>(),
                        biWidth = Width,
                        biHeight = -Height, // Top-down image
                        biPlanes = 1,
                        biBitCount = 32,
                        biCompression = 0, // BI_RGB
                        biSizeImage = 0
                    }
                };
                _hBitmap = Gdi32.CreateDIBSection(_hdcDest, bmi, Gdi32.DIBColorMode.DIB_RGB_COLORS, out var bits,
                    IntPtr.Zero);

                if (_hBitmap.IsInvalid || bits == 0)
                {
                    if (!_hBitmap.IsInvalid)
                    {
                        Gdi32.DeleteObject(_hBitmap);
                    }

                    throw new Exception($"Failed to create dIB section for {_hWnd}");
                }

                _bitsPtr = bits;
                _oldBitmap = Gdi32.SelectObject(_hdcDest, _hBitmap);
                if (_oldBitmap.IsNull)
                {
                    throw new Exception("Failed to select object");
                }
            }
            catch (Exception)
            {
                ReleaseResources();
                throw;
            }
        }
    }

    /// <summary>
    /// 调用bitblt复制并返回新的mat
    /// </summary>
    /// <returns></returns>
    public Mat? BitBlt()
    {
        lock (LockObject)
        {
            return Gdi32.BitBlt(_hdcDest, 0, 0, Width, Height, _hdcSrc, 0, 0, Gdi32.RasterOperationMode.SRCCOPY)
                ? Mat.FromPixelData(Height, Width, MatType.CV_8UC4, _bitsPtr)
                : null;
        }
    }
    
    public bool IsInvalid()
    {
        return _hWnd.IsNull || _hdcSrc.IsInvalid || _hdcDest.IsInvalid || _hBitmap.IsInvalid || _bitsPtr == IntPtr.Zero;
    }


    public void Dispose()
    {
        lock (LockObject)
        {
            ReleaseResources();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// 需加锁环境下访问，释放资源
    /// </summary>
    private void ReleaseResources()
    {
        if (!_oldBitmap.IsNull)
        {
            // 先选回资源再释放_hBitmap
            Gdi32.SelectObject(_hdcDest, _oldBitmap);
            _oldBitmap = Gdi32.SafeHBITMAP.Null;
        }

        
        if (!_hBitmap.IsNull)
        {
            Gdi32.DeleteObject(_hBitmap);
            _hBitmap = Gdi32.SafeHBITMAP.Null;
        }


        if (!_hdcDest.IsNull)
        {
            Gdi32.DeleteDC(_hdcDest);
            _hdcDest = Gdi32.SafeHDC.Null;
        }

        if (_hdcSrc != IntPtr.Zero)
        {
            if (_hWnd.IsNull)
            {
                Gdi32.DeleteDC(_hdcDest);
            }
            else
            {
                User32.ReleaseDC(_hWnd, _hdcSrc);
            }


            _hdcSrc = User32.SafeReleaseHDC.Null;
        }

        _bitsPtr = IntPtr.Zero;
    }
}