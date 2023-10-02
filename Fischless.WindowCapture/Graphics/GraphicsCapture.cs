using System.Diagnostics;
using System.Windows;
using Vanara.PInvoke;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace Fischless.WindowCapture.Graphics;

public class GraphicsCapture : IWindowCapture
{
    private nint _hWnd;

    private Direct3D11CaptureFramePool _captureFramePool = null!;
    private GraphicsCaptureItem _captureItem = null!;
    private GraphicsCaptureSession _captureSession = null!;

    public bool IsCapturing { get; private set; }
    public bool IsClientEnabled { get; set; } = false;

    public void Dispose()
    {
        Stop();
    }

    public void Start(nint hWnd)
    {
        _hWnd = hWnd;
        IsCapturing = true;

        _captureItem = CaptureHelper.CreateItemForWindow(_hWnd);

        if (_captureItem == null)
        {
            throw new InvalidOperationException("Failed to create capture item.");
        }

        _captureItem.Closed += CaptureItemOnClosed;

        var device = Direct3D11Helper.CreateDevice();

        _captureFramePool = Direct3D11CaptureFramePool.Create(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2,
            _captureItem.Size);
        _captureSession = _captureFramePool.CreateCaptureSession(_captureItem);
        _captureSession.IsCursorCaptureEnabled = false;
        _captureSession.IsBorderRequired = false;
        _captureSession.StartCapture();
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
            using var frame = _captureFramePool?.TryGetNextFrame();

            if (frame == null)
            {
                return null;
            }

            Bitmap? bitmap = frame.ToBitmap();

            if (bitmap == null)
            {
                return null;
            }

            if (IsClientEnabled)
            {
                return bitmap;
            }
            else
            {
                _ = User32.GetClientRect(_hWnd, out var windowRect);
                int border = Math.Max(SystemParameters.Border, 1);
                int captionHeight = CaptionHelper.IsFullScreenMode(_hWnd)
                    ? default
                    : Math.Max(CaptionHelper.GetSystemCaptionHeight(), frame.ContentSize.Height - windowRect.Height - border * 2);

                using (bitmap)
                {
                    return bitmap.Crop(
                        border,
                        border + captionHeight,
                        frame.ContentSize.Width - border * 2,
                        frame.ContentSize.Height - border * 2 - captionHeight
                    );
                }
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
        return null;
    }

    public Bitmap? Capture(int x, int y, int width, int height)
    {
        if (_hWnd == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var frame = _captureFramePool?.TryGetNextFrame();
            using Bitmap bitmap = frame?.ToBitmap();
            _ = User32.GetClientRect(_hWnd, out var windowRect);
            int border = Math.Max(SystemParameters.Border, 1);
            int captionHeight = CaptionHelper.IsFullScreenMode(_hWnd)
                ? default
                : Math.Max(CaptionHelper.GetSystemCaptionHeight()
                    , frame.ContentSize.Height - windowRect.Height - border * 2
            );

            return bitmap.Crop(
                border + x,
                border + y + captionHeight,
                width - border * 2,
                height - border * 2 - captionHeight
            );
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
        }
        return null;
    }

    public void Stop()
    {
        _captureSession?.Dispose();
        _captureFramePool?.Dispose();
        _captureSession = null!;
        _captureFramePool = null!;
        _captureItem = null!;

        _hWnd = IntPtr.Zero;
        IsCapturing = false;
    }

    private void CaptureItemOnClosed(GraphicsCaptureItem sender, object args)
    {
        Stop();
    }
}
