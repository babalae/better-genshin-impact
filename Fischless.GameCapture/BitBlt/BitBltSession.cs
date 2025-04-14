using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using Vanara.PInvoke;

namespace Fischless.GameCapture.BitBlt;

public class BitBltSession : IDisposable
{
    // 用于过滤alpha通道
    private static readonly Gdi32.BLENDFUNCTION BlendFunction = new()
    {
        BlendOp = 0, //Gdi32.BlendOperation.AC_SRC_OVER,
        BlendFlags = 0,
        SourceConstantAlpha = 255,
        AlphaFormat = 0
    };

    private readonly int _height;

    // 窗口句柄
    private readonly HWND _hWnd;

    private readonly object _lockObject = new();

    // 大小计算好的宽高，截图用这个不会爆炸
    private readonly int _width;

    // 位图指针，这个指针会在位图释放时自动释放
    private IntPtr _bitsPtr;

    // 位图句柄
    private Gdi32.SafeHBITMAP _hBitmap;

    // 缓冲区 CompatibleDC
    private Gdi32.SafeHDC _hdcDest;

    // 来源DC
    private User32.SafeReleaseHDC _hdcSrc;

    // 旧位图，析构时一起释放掉
    private HGDIOBJ _oldBitmap;


    public BitBltSession(HWND hWnd, int w, int h)
    {
        // 分配全空对象 避免 System.NullReferenceException
        // 不能直接在上面写，不然zz编译器会报warning,感觉抑制warning也不优雅
        _oldBitmap = Gdi32.SafeHBITMAP.Null;
        _hBitmap = Gdi32.SafeHBITMAP.Null;
        _hdcDest = Gdi32.SafeHDC.Null;
        _hdcSrc = User32.SafeReleaseHDC.Null;
        _bitsPtr = IntPtr.Zero;

        if (hWnd.IsNull) throw new Exception("hWnd is invalid");

        _hWnd = hWnd;

        if (w <= 0 || h <= 0) throw new Exception("Invalid width or height");

        // 这俩仅做标记
        Width = w;
        Height = h;

        lock (_lockObject)
        {
            try
            {
                _hdcSrc = User32.GetDC(_hWnd);
                if (_hdcSrc.IsInvalid) throw new Exception("Failed to get DC for {_hWnd}");

                var hdcSrcPixel = Gdi32.GetDeviceCaps(_hdcSrc, Gdi32.DeviceCap.BITSPIXEL);
                // 颜色位数
                if (hdcSrcPixel != 32)
                    // 目前只考虑支持32位RGBA,HDR什么的先放放
                    throw new Exception("BitBlt only support 32 bit RGBA pixel color");

                var hdcSrcPlanes = Gdi32.GetDeviceCaps(_hdcSrc, Gdi32.DeviceCap.PLANES);
                // 颜色平面数
                if (hdcSrcPlanes > 1)
                    // 意味着色深不是标准色深
                    throw new Exception("BitBlt only support 1 plane");

                var hdcClip = Gdi32.GetDeviceCaps(_hdcSrc, Gdi32.DeviceCap.CLIPCAPS);
                if (hdcClip == 0)
                    // 设备不支持剪切出单一窗口
                    throw new Exception("Device does not support clipping");

                _hdcDest = Gdi32.CreateCompatibleDC(_hdcSrc);
                var maxW = Gdi32.GetDeviceCaps(_hdcDest, Gdi32.DeviceCap.HORZRES);
                var maxH = Gdi32.GetDeviceCaps(_hdcDest, Gdi32.DeviceCap.VERTRES);

                if (maxW <= 0 || maxH <= 0)
                    // 显示器不见啦
                    throw new Exception("Can not get display size");

                // 避免截取全屏窗口的时候超出CompatibleDC范围
                _width = Math.Min(maxW, Width);
                _height = Math.Min(maxH, Height);

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
                        biWidth = _width,
                        biHeight = -_height, // Top-down image
                        biPlanes = (ushort)hdcSrcPlanes,
                        biBitCount = (ushort)(hdcSrcPixel - 8), //RGBA->RGB
                        biCompression = 0, // BI_RGB
                        biSizeImage = 0
                    }
                };
                _hBitmap = Gdi32.CreateDIBSection(_hdcDest, bmi, Gdi32.DIBColorMode.DIB_RGB_COLORS, out var bits,
                    IntPtr.Zero);

                if (_hBitmap.IsInvalid || bits == 0)
                {
                    if (!_hBitmap.IsInvalid) Gdi32.DeleteObject(_hBitmap);

                    throw new Exception($"Failed to create dIB section for {_hWnd}");
                }

                _bitsPtr = bits;
                _oldBitmap = Gdi32.SelectObject(_hdcDest, _hBitmap);
                if (_oldBitmap.IsNull) throw new Exception("Failed to select object");
            }
            catch (Exception)
            {
                ReleaseResources();
                throw;
            }
            finally
            {
                Gdi32.GdiFlush();
            }
        }
    }

    //原宽高，用来喂上层
    public int Width { get; }
    public int Height { get; }


    public void Dispose()
    {
        lock (_lockObject)
        {
            ReleaseResources();
            GC.SuppressFinalize(this);
        }
    }


    /// <summary>
    ///     调用GDI复制到缓冲区并返回新Image
    /// </summary>
    public Bitmap? GetImage()
    {
        lock (_lockObject)
        {
            Gdi32.GdiFlush();
            if (_hBitmap.IsInvalid)
                // 位图指针无效
                return null;

            Gdi32.BITMAP bitmap;
            try
            {
                // 获取位图详情，这应该能抵挡住奇奇怪怪的bug
                bitmap = Gdi32.GetObject<Gdi32.BITMAP>(_hBitmap);
            }
            catch (ArgumentException)
            {
                // 可能是位图已经被释放了
                return null;
            }

            // 这个是bitmap的结构信息，不用手动释放
            if (bitmap.bmPlanes != 1 || bitmap.bmBitsPixel != 24)
                // 不支持的位图格式
                return null;
            // 直接返回转换后的位图
            var success = Gdi32.AlphaBlend(_hdcDest, 0, 0, _width, _height, _hdcSrc, 0, 0,
                _width, _height, BlendFunction);
            if (!success || !Gdi32.GdiFlush()) return null;

            // return Mat.FromPixelData(bitmap.bmHeight, bitmap.bmWidth, MatType.CV_8UC3, bitmap.bmBits,bitmap.bmWidthBytes);
            return _hBitmap.ToBitmap();

            // 原始宏 ((((biWidth * biBitCount) + 31) & ~31) >> 3) => (biWidth * biBitCount + 3) & ~3) (在总位数是8的倍数时，两者等价)
            // 对齐 https://learn.microsoft.com/zh-cn/windows/win32/api/wingdi/ns-wingdi-bitmapinfoheader

            //  适用于32位(RGBA)
            //  return Gdi32.BitBlt(_hdcDest, 0, 0, Width, Height, _hdcSrc, 0, 0, Gdi32.RasterOperationMode.SRCCOPY ) ? Mat.FromPixelData(Height, Width, MatType.CV_8UC3, _bitsPtr) : null;
        }
    }


    /// <summary>
    ///     不是所有的失效情况都能被检测到
    /// </summary>
    /// <returns></returns>
    public bool IsInvalid()
    {
        return _hWnd.IsNull || _hdcSrc.IsInvalid || _hdcDest.IsInvalid || _hBitmap.IsInvalid || _bitsPtr == IntPtr.Zero;
    }

    /// <summary>
    ///     需加锁环境下访问，释放资源
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

        Gdi32.GdiFlush();
        _bitsPtr = IntPtr.Zero;
    }
}