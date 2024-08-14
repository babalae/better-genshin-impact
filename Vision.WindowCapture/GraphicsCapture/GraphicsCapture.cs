using System.Drawing;
using Vision.WindowCapture.GraphicsCapture.Helpers;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Win32.Foundation;

namespace Vision.WindowCapture.GraphicsCapture;

public class GraphicsCapture : IWindowCapture
{
    private HWND _hWnd;

    private Direct3D11CaptureFramePool? _captureFramePool;
    private GraphicsCaptureItem? _captureItem;
    private GraphicsCaptureSession? _captureSession;

    public bool IsCapturing { get; private set; }

    public async Task StartAsync(HWND hWnd)
    {
        _hWnd = hWnd;
        /*
        // if use GraphicsCapturePicker, need to set this window handle
        // new WindowInteropHelper(this).Handle
        var picker = new GraphicsCapturePicker();
        picker.SetWindow(hWnd);
        _captureItem = picker.PickSingleItemAsync().AsTask().Result;
        */

        var device = Direct3D11Helper.CreateDevice();

        _captureItem = CaptureHelper.CreateItemForWindow(_hWnd) ?? throw new InvalidOperationException("Failed to create capture item.");

        var size = _captureItem.Size;

        if (size.Width < 480 || size.Height < 360)
        {
            throw new InvalidOperationException("窗口画面大小小于480x360，无法使用");
        }

        _captureItem.Closed += OnCaptureItemClosed;

        _captureFramePool = Direct3D11CaptureFramePool.Create(device, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
        _captureSession = _captureFramePool.CreateCaptureSession(_captureItem);
        if (ApiInformation.IsWriteablePropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            _captureSession.IsBorderRequired = false;
            _captureSession.IsCursorCaptureEnabled = false;
        }

        _captureSession.StartCapture();
        IsCapturing = true;
    }

    /// <summary>
    /// How to handle window size changes?
    /// </summary>
    /// <returns></returns>
    public Bitmap? Capture()
    {
        using var frame = _captureFramePool?.TryGetNextFrame();
        return frame?.ToBitmap();
    }

    public Task StopAsync()
    {
        _captureSession?.Dispose();
        _captureFramePool?.Dispose();
        _captureSession = default;
        _captureFramePool = default;
        _captureItem = default;

        _hWnd = HWND.Null;
        IsCapturing = false;
        return Task.CompletedTask;
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args)
    {
        StopAsync();
    }
}