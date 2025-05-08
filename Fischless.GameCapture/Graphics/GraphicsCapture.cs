using System.Diagnostics;
using Fischless.GameCapture.Graphics.Helpers;
using SharpDX.Direct3D11;
using Vanara.PInvoke;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using OpenCvSharp;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace Fischless.GameCapture.Graphics;

public class GraphicsCapture : IGameCapture
{
    private nint _hWnd;

    private Direct3D11CaptureFramePool _captureFramePool = null!;
    private GraphicsCaptureItem _captureItem = null!;

    private GraphicsCaptureSession _captureSession = null!;

    private IDirect3DDevice _d3dDevice = null!;

    public bool IsCapturing { get; private set; }

    private ResourceRegion? _region;

    // HDR相关
    private bool _isHdrEnabled;
    private DirectXPixelFormat _pixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;


    // 最新帧的存储
    private Mat? _latestFrame;
    private readonly ReaderWriterLockSlim _frameAccessLock = new();

    // 用于获取帧数据的临时纹理和暂存资源
    private Texture2D? _stagingTexture;

    private long _lastFrameTime = 0;

    private readonly Stopwatch _frameTimer = new Stopwatch();

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

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


        // 创建D3D设备
        _d3dDevice = Direct3D11Helper.CreateDevice();

        // 检测HDR状态
        _isHdrEnabled = false;
        _pixelFormat = _isHdrEnabled ? DirectXPixelFormat.R16G16B16A16Float : DirectXPixelFormat.B8G8R8A8UIntNormalized;

        // 创建帧池
        _captureFramePool = Direct3D11CaptureFramePool.Create(
            _d3dDevice,
            _pixelFormat,
            2,
            _captureItem.Size);


        _captureItem.Closed += CaptureItemOnClosed;
        _captureFramePool.FrameArrived += OnFrameArrived;

        _captureSession = _captureFramePool.CreateCaptureSession(_captureItem);
        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession",
                "IsCursorCaptureEnabled"))
        {
            _captureSession.IsCursorCaptureEnabled = false;
        }

        if (ApiInformation.IsWriteablePropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession",
                "IsBorderRequired"))
        {
            _captureSession.IsBorderRequired = false;
        }

        _frameTimer.Start();
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
        DwmApi.DwmGetWindowAttribute<RECT>(hWnd, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            out var windowRect);
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

    public static bool IsHdrEnabled(nint hWnd)
    {
        try
        {
            var hdc = User32.GetDC(hWnd);
            if (hdc != IntPtr.Zero)
            {
                int bitsPerPixel = Gdi32.GetDeviceCaps(hdc, Gdi32.DeviceCap.BITSPIXEL);
                User32.ReleaseDC(hWnd, hdc);

                // 如果位深大于等于32位，认为支持HDR
                return bitsPerPixel >= 32;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private Texture2D CreateStagingTexture(Direct3D11CaptureFrame frame, Device device)
    {
        // 创建可以用于CPU读取的暂存纹理
        var textureDesc = new Texture2DDescription
        {
            CpuAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            Format = _isHdrEnabled ? Format.R16G16B16A16_Float : Format.B8G8R8A8_UNorm,
            Width = _region == null ? frame.ContentSize.Width : _region.Value.Right - _region.Value.Left,
            Height = _region == null ? frame.ContentSize.Height : _region.Value.Bottom - _region.Value.Top,
            OptionFlags = ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = { Count = 1, Quality = 0 },
            Usage = ResourceUsage.Staging
        };

        return new Texture2D(device, textureDesc);
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        using var frame = sender.TryGetNextFrame();
        if (frame == null)
        {
            return;
        }

        // 限制最高处理帧率为62fps
        if (_frameTimer.ElapsedMilliseconds - _lastFrameTime < 16)
        {
            return;
        }
        _lastFrameTime = _frameTimer.ElapsedMilliseconds;

        var frameSize = _captureItem.Size;

        // 检查帧大小是否变化 // 不会被访问到的代码
        if (frameSize.Width != frame.ContentSize.Width ||
            frameSize.Height != frame.ContentSize.Height)
        {
            frameSize = frame.ContentSize;
            _captureFramePool.Recreate(
                _d3dDevice,
                _pixelFormat,
                2,
                frameSize
            );
            _stagingTexture?.Dispose();
            _stagingTexture = null;
        }

        // 从捕获的帧创建一个可以被访问的纹理
        using var surfaceTexture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
        var d3dDevice = surfaceTexture.Device;

        _stagingTexture ??= CreateStagingTexture(frame, d3dDevice);
        var mat = _stagingTexture.CreateMat(d3dDevice, surfaceTexture, _region, _isHdrEnabled);
        
        // 使用写锁更新最新帧
        _frameAccessLock.EnterWriteLock();
        try
        {
            // 释放之前的帧
            _latestFrame?.Dispose();
            // 更新
            _latestFrame = mat;
        }
        finally
        {
            _frameAccessLock.ExitWriteLock();
        }
    }

    public Mat? Capture()
    {
        // 使用读锁获取最新帧
        _frameAccessLock.EnterReadLock();
        try
        {
            // 返回最新帧的副本（这里我们必须克隆，因为Mat是不线程安全的）
            return _latestFrame?.Clone();
        }
        finally
        {
            _frameAccessLock.ExitReadLock();
        }
    }

    public void Stop()
    {
        _frameAccessLock.EnterWriteLock();
        try
        {
            _captureSession?.Dispose();
            _captureFramePool?.Dispose();
            _captureSession = null!;
            _captureFramePool = null!;
            _captureItem = null!;
            _stagingTexture?.Dispose();
            _d3dDevice?.Dispose();

            _hWnd = IntPtr.Zero;
            IsCapturing = false;
        }
        finally
        {
            _frameAccessLock.ExitWriteLock();
        }
    }

    private void CaptureItemOnClosed(GraphicsCaptureItem sender, object args)
    {
        Stop();
    }
}