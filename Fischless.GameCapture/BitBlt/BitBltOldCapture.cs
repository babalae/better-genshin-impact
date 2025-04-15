using System.Diagnostics;
using Vanara.PInvoke;

namespace Fischless.GameCapture.BitBlt;

public class BitBltOldCapture : IGameCapture
{
    private nint _hWnd;

    public static object LockObject { get; } = new();

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

    public CaptureImageRes? Capture()
    {
        if (_hWnd == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            lock (LockObject)
            {
                User32.GetClientRect(_hWnd, out var windowRect);
                int x = default, y = default;
                var width = windowRect.right - windowRect.left;
                var height = windowRect.bottom - windowRect.top;

                Bitmap bitmap = new(width, height);
                using System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap);
                var hdcDest = g.GetHdc();
                var hdcSrc = User32.GetDC(_hWnd == IntPtr.Zero ? User32.GetDesktopWindow() : _hWnd);
                Gdi32.StretchBlt(hdcDest, 0, 0, width, height, hdcSrc, x, y, width, height,
                    Gdi32.RasterOperationMode.SRCCOPY);
                g.ReleaseHdc();
                Gdi32.DeleteDC(hdcDest);
                Gdi32.DeleteDC(hdcSrc);
                return CaptureImageRes.BuildNullable(bitmap);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }

        return null!;
    }

    public void Stop()
    {
        _hWnd = IntPtr.Zero;
        IsCapturing = false;
    }
}