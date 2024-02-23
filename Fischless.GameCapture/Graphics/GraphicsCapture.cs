using Fischless.GameCapture.Graphics.Helpers;
using SharpDX.Direct3D11;
using System.Diagnostics;
using Vanara.PInvoke;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace Fischless.GameCapture.Graphics;

public class GraphicsCapture : IGameCapture
{
    private nint _hWnd;

    private Direct3D11CaptureFramePool _captureFramePool = null!;
    private GraphicsCaptureItem _captureItem = null!;
    private GraphicsCaptureSession _captureSession = null!;

    public CaptureModes Mode => CaptureModes.WindowsGraphicsCapture;
    public bool IsCapturing { get; private set; }

    private ResourceRegion? _region;

    private bool _useBitmapCache = false;
    private Bitmap? _currentBitmap;

    public void Dispose() => Stop();

    public void Start(nint hWnd, Dictionary<string, object>? settings = null)
    {
        _hWnd = hWnd;

        _region = GetGameScreenRegion(hWnd);

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

        if (settings != null && settings.TryGetValue("useBitmapCache", out object? value) && (bool)value)
        {
            _useBitmapCache = true;
            _captureFramePool.FrameArrived += OnFrameArrived;
        }

        _captureSession = _captureFramePool.CreateCaptureSession(_captureItem);
        _captureSession.IsCursorCaptureEnabled = false;
        if (ApiInformation.IsWriteablePropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired"))
        {
            _captureSession.IsBorderRequired = false;
        }

        _captureSession.StartCapture();
        IsCapturing = true;
    }

    /// <summary>
    /// 从 DwmGetWindowAttribute 的矩形 截取出 GetClientRect的矩形（游戏区域）
    /// </summary>
    /// <param name="hWnd"></param>
    /// <returns></returns>
    private ResourceRegion? GetGameScreenRegion(nint hWnd)
    {
        var exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
        if ((exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0)
        {
            return null;
        }


        ResourceRegion region = new();
        DwmApi.DwmGetWindowAttribute<RECT>(hWnd, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, out var windowRect);
        User32.GetClientRect(_hWnd, out var clientRect);
        //POINT point = default; // 这个点和 DwmGetWindowAttribute 结果差1
        //User32.ClientToScreen(hWnd, ref point);

        region.Left = 0;
        region.Top = windowRect.Height - clientRect.Height;
        region.Right = clientRect.Width;
        region.Bottom = windowRect.Height;
        region.Front = 0;
        region.Back = 1;

        return region;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = _captureFramePool?.TryGetNextFrame();

        if (frame != null)
        {
            var b = frame.ToBitmap(_region);
            if (b != null)
            {
                _currentBitmap = b;
            }
        }
    }

    public Bitmap? Capture()
    {
        if (_hWnd == IntPtr.Zero)
        {
            return null;
        }

        if (!_useBitmapCache)
        {
            try
            {
                using var frame = _captureFramePool?.TryGetNextFrame();

                if (frame == null)
                {
                    return null;
                }

                return frame.ToBitmap(_region);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }

            return null;
        }
        else
        {
            if (_currentBitmap == null)
            {
                return null;
            }

            return (Bitmap)_currentBitmap.Clone();
        }
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