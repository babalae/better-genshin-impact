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

    // 用于过滤alpha通道
    private static readonly Gdi32.BLENDFUNCTION BlendFunction = new()
    {
        BlendOp = 0, //Gdi32.BlendOperation.AC_SRC_OVER,
        BlendFlags = 0,
        SourceConstantAlpha = 255,
        AlphaFormat = 0, //Gdi32.AlphaFormat.AC_SRC_ALPHA
    };

    private readonly object _lockObject = new object();


    public BitBltSession(HWND hWnd, int w, int h)
    {
        lock (_lockObject)
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
                        biBitCount = 24,
                        biCompression = 0, // BI_RGB
                        biSizeImage = 0,
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
    /// 调用GDI复制到缓冲区并返回新的mat
    /// </summary>
    /// <returns>MatType.CV_8UC3</returns>
    public Mat? GetMat()
    {
        lock (_lockObject)
        {
            // 直接返回转换后的位图
            return Gdi32.AlphaBlend(_hdcDest, 0, 0, Width, Height, _hdcSrc, 0, 0, Width, Height, BlendFunction)
                ? Mat.FromPixelData(Height, Width, MatType.CV_8UC3, _bitsPtr, (Width * 3 + 3) & ~3)
                : null;

            // 原始宏 ((((biWidth * biBitCount) + 31) & ~31) >> 3) => (biWidth * biBitCount + 3) & ~3) (在总位数是8的倍数时，两者等价)
            // 对齐 https://learn.microsoft.com/zh-cn/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader

            //  适用于32位(RGBA)
            //  return Gdi32.BitBlt(_hdcDest, 0, 0, Width, Height, _hdcSrc, 0, 0, Gdi32.RasterOperationMode.SRCCOPY ) ? Mat.FromPixelData(Height, Width, MatType.CV_8UC3, _bitsPtr) : null;
        }
    }


    /// <summary>
    /// 不是所有的失效情况都能被检测到
    /// 
    /// </summary>
    /// <returns></returns>
    public bool IsInvalid()
    {
        return _hWnd.IsNull || _hdcSrc.IsInvalid || _hdcDest.IsInvalid || _hBitmap.IsInvalid || _bitsPtr == IntPtr.Zero;
    }


    public void Dispose()
    {
        lock (_lockObject)
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
            User32.ReleaseDC(_hWnd, _hdcSrc);
            _hdcSrc = User32.SafeReleaseHDC.Null;
        }

        _bitsPtr = IntPtr.Zero;
    }
}