using System.Diagnostics;
using Fischless.GameCapture.Graphics.Helpers;
using SharpDX.Direct3D11;
using Vanara.PInvoke;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using OpenCvSharp;
using SharpDX;
using SharpDX.D3DCompiler;

namespace Fischless.GameCapture.Graphics;

public class GraphicsCapture(bool captureHdr = false) : IGameCapture
{
    private nint _hWnd;

    private Direct3D11CaptureFramePool? _captureFramePool;
    private GraphicsCaptureItem? _captureItem;

    private GraphicsCaptureSession? _captureSession;

    private IDirect3DDevice? _d3dDevice;

    public bool IsCapturing { get; private set; }

    private ResourceRegion? _region;

    // HDR相关
    private bool _isHdrEnabled = captureHdr;
    private DirectXPixelFormat _pixelFormat;
    private Texture2D? _hdrOutputTexture;
    private ComputeShader? _hdrComputeShader;

    // 最新帧的存储
    private Mat? _latestFrame;
    private readonly ReaderWriterLockSlim _frameAccessLock = new();

    // 用于获取帧数据的临时纹理和暂存资源
    private Texture2D? _stagingTexture;

    // Surface 大小
    private int _surfaceWidth;
    private int _surfaceHeight;

    private long _lastFrameTime;

    private readonly Stopwatch _frameTimer = new();

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

        _surfaceWidth = _captureItem.Size.Width;
        _surfaceHeight = _captureItem.Size.Height;

        // 创建D3D设备
        _d3dDevice = Direct3D11Helper.CreateDevice();

        // 创建帧池
        try
        {
            if (!_isHdrEnabled)
            {
                // 不处理 HDR，直接抛异常走 SDR 分支
                throw new Exception();
            }

            _pixelFormat = DirectXPixelFormat.R16G16B16A16Float;
            _captureFramePool = Direct3D11CaptureFramePool.Create(
                _d3dDevice,
                _pixelFormat,
                2,
                _captureItem.Size);
        }
        catch (Exception)
        {
            // Fallback
            _pixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
            _captureFramePool = Direct3D11CaptureFramePool.Create(
                _d3dDevice,
                _pixelFormat,
                2,
                _captureItem.Size);
            _isHdrEnabled = false;
        }


        _captureItem.Closed += CaptureItemOnClosed;
        _captureFramePool.FrameArrived += OnFrameArrived;

        _captureSession = _captureFramePool.CreateCaptureSession(_captureItem);
        if (ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession",
                nameof(GraphicsCaptureSession.IsCursorCaptureEnabled)))
        {
            _captureSession.IsCursorCaptureEnabled = false;
        }

        if (ApiInformation.IsWriteablePropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession",
                nameof(GraphicsCaptureSession.IsBorderRequired)))
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
    private static ResourceRegion? GetGameScreenRegion(nint hWnd)
    {
        var exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
        if ((exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0)
        {
            return null;
        }

        ResourceRegion region = new();
        DwmApi.DwmGetWindowAttribute<RECT>(hWnd, DwmApi.DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS,
            out var windowRect);
        User32.GetClientRect(hWnd, out var clientRect);
        POINT point = default;
        User32.ClientToScreen(hWnd, ref point);

        region.Left = point.X > windowRect.Left ? point.X - windowRect.Left : 0;
        region.Top = point.Y > windowRect.Top ? point.Y - windowRect.Top : 0;
        region.Right = region.Left + clientRect.Width;
        region.Bottom = region.Top + clientRect.Height;
        region.Front = 0;
        region.Back = 1;

        return region;
    }

    private Texture2D ProcessHdrTexture(Texture2D hdrTexture)
    {
        var device = hdrTexture.Device;
        var context = device.ImmediateContext;

        var width = hdrTexture.Description.Width;
        var height = hdrTexture.Description.Height;

        _hdrOutputTexture ??= Direct3D11Helper.CreateOutputTexture(device, width, height);
        _hdrComputeShader ??= new ComputeShader(device, ShaderBytecode.Compile(HdrToSdrShader.Content, "CS_HDRtoSDR", "cs_5_0"));

        using var inputSrv = new ShaderResourceView(device, hdrTexture);
        using var outputUav = new UnorderedAccessView(device, _hdrOutputTexture);

        context.ComputeShader.Set(_hdrComputeShader);
        context.ComputeShader.SetShaderResource(0, inputSrv);
        context.ComputeShader.SetUnorderedAccessView(0, outputUav);

        var threadGroupCountX = (int)Math.Ceiling(width / 16.0);
        var threadGroupCountY = (int)Math.Ceiling(height / 16.0);

        context.Dispatch(threadGroupCountX, threadGroupCountY, 1);

        return _hdrOutputTexture;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        // 使用写锁更新最新帧
        _frameAccessLock.EnterWriteLock();
        try
        {
            if (_hWnd == 0)
            {
                return;
            }

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

            var captureSize = _captureItem!.Size;

            // 检查帧大小是否变化
            if (captureSize.Width != _surfaceWidth || captureSize.Height != _surfaceHeight)
            {
                if (User32.IsIconic(_hWnd))
                    return;

                _captureFramePool!.Recreate(
                    _d3dDevice,
                    _pixelFormat,
                    2,
                    captureSize
                );
                _stagingTexture?.Dispose();
                _stagingTexture = null;
                _surfaceWidth = captureSize.Width;
                _surfaceHeight = captureSize.Height;
                _region = GetGameScreenRegion(_hWnd);
                return;
            }

            try
            {
                // 从捕获的帧创建一个可以被访问的纹理
                using var surfaceTexture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
                var sourceTexture = _isHdrEnabled ? ProcessHdrTexture(surfaceTexture) : surfaceTexture;
                var d3dDevice = surfaceTexture.Device;

                _stagingTexture ??= Direct3D11Helper.CreateStagingTexture(d3dDevice, frame.ContentSize.Width, frame.ContentSize.Height, _region);
                var mat = _stagingTexture.CreateMat(d3dDevice, sourceTexture, _region);

                // 释放之前的帧，然后更新
                _latestFrame?.Dispose();
                _latestFrame = mat;
            }
            catch (SharpDXException e)
            {
                Debug.WriteLine($"SharpDXException: {e.Descriptor}");
                _latestFrame = null;
            }
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
            _captureSession = null;
            _captureFramePool?.Dispose();
            _captureFramePool = null;
            _captureItem = null;
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _hdrOutputTexture?.Dispose();
            _hdrOutputTexture = null;
            _hdrComputeShader?.Dispose();
            _hdrComputeShader = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
            _hWnd = 0;
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
