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
    private readonly bool _captureHdrRequested = captureHdr;
    private nint _hWnd;

    private Direct3D11CaptureFramePool? _captureFramePool;
    private GraphicsCaptureItem? _captureItem;

    private GraphicsCaptureSession? _captureSession;

    private IDirect3DDevice? _d3dDevice;
    private SharpDX.Direct3D11.Device? _sharpDxDevice;
    private DeviceContext? _d3dContext;

    public bool IsCapturing { get; private set; }

    private ResourceRegion? _region;
    private RECT? _captureRect;

    // HDR相关
    private bool _isHdrEnabled = captureHdr;
    private float _hdrSdrWhiteScale = HdrDisplayInformation.FallbackSdrWhiteScale;
    private DirectXPixelFormat _pixelFormat;
    private Texture2D? _hdrOutputTexture;
    private ComputeShader? _hdrComputeShader;
    private SharpDX.Direct3D11.Buffer? _hdrParametersBuffer;

    // 最新帧的存储
    private Mat? _latestFrame;
    private readonly ReaderWriterLockSlim _frameAccessLock = new();

    // 用于获取帧数据的临时纹理和暂存资源
    private Texture2D? _stagingTexture;

    // Surface 大小
    private int _surfaceWidth;
    private int _surfaceHeight;

    // HDR 管线依赖窗口所在的显示器；跨屏移动后由窗口事件设置请求，并在帧回调中安全重建帧池。
    private IntPtr _hdrDisplayMonitor;
    private int _hdrDisplayRefreshPending;

    private long _lastFrameTime;

    private readonly Stopwatch _frameTimer = new();

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    public void Start(nint hWnd, Dictionary<string, object>? settings = null)
    {
        Stop();
        try
        {
            _hWnd = hWnd;
            (_region, _captureRect) = GetGameScreenInfo(hWnd);
            _hdrDisplayMonitor = IntPtr.Zero;
            Volatile.Write(ref _hdrDisplayRefreshPending, 0);

            if (_captureHdrRequested)
            {
                var displayState = HdrDisplayInformation.GetState(hWnd);
                var pipelineDecision = ResolveHdrPipeline(displayState);
                _isHdrEnabled = pipelineDecision.IsHdrEnabled;
                _hdrSdrWhiteScale = pipelineDecision.SdrWhiteScale;
                _hdrDisplayMonitor = GetMonitorHandle(hWnd);
            }
            else
            {
                _isHdrEnabled = false;
                _hdrSdrWhiteScale = 1f;
            }

            _captureItem = CaptureHelper.CreateItemForWindow(_hWnd);

            if (_captureItem == null)
            {
                throw new InvalidOperationException("Failed to create capture item.");
            }

            _surfaceWidth = _captureItem.Size.Width;
            _surfaceHeight = _captureItem.Size.Height;

            // 每个会话独占 device/context，避免多实例共享 immediate context。
            _d3dDevice = Direct3D11Helper.CreateDevice(out _sharpDxDevice);
            _d3dContext = _sharpDxDevice.ImmediateContext;

            // 仅在目标显示器实际启用 HDR 时创建 FP16 管线。
            if (_isHdrEnabled)
            {
                _pixelFormat = DirectXPixelFormat.R16G16B16A16Float;
            }
            else
            {
                _pixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
            }

            _captureFramePool = Direct3D11CaptureFramePool.Create(
                _d3dDevice,
                _pixelFormat,
                2,
                _captureItem.Size);
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

            _lastFrameTime = 0;
            _frameTimer.Restart();
            _captureSession.StartCapture();
            IsCapturing = true;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    internal static HdrPipelineDecision ResolveHdrPipeline(HdrDisplayState displayState)
    {
        return displayState.Kind switch
        {
            HdrDisplayStateKind.Sdr => new HdrPipelineDecision(false, 1f),
            HdrDisplayStateKind.Hdr => new HdrPipelineDecision(true, displayState.SdrWhiteScale),
            HdrDisplayStateKind.HdrWhiteLevelUnavailable =>
                new HdrPipelineDecision(true, HdrDisplayInformation.FallbackSdrWhiteScale),
            _ => new HdrPipelineDecision(true, HdrDisplayInformation.FallbackSdrWhiteScale),
        };
    }

    /// <summary>
    /// 通知捕获器窗口位置发生变化。此方法只设置原子标记；显示器查询和帧池重建均延后到
    /// 帧回调，避免阻塞 WinEventHook 线程。
    /// </summary>
    public void NotifyWindowLocationChanged(nint hWnd)
    {
        if (!_captureHdrRequested || !IsCapturing || hWnd != _hWnd)
        {
            return;
        }

        // 多次位置事件只保留一个待处理标记；帧回调会查询当前监视器，避免使用过期目标。
        Volatile.Write(ref _hdrDisplayRefreshPending, 1);
    }

    /// <summary>
    /// 从 DwmGetWindowAttribute 的矩形 截取出 GetClientRect的矩形（游戏区域）
    /// </summary>
    /// <param name="hWnd"></param>
    /// <returns></returns>
    private static (ResourceRegion? Region, RECT? CaptureRect) GetGameScreenInfo(nint hWnd)
    {
        var exStyle = User32.GetWindowLong(hWnd, User32.WindowLongFlags.GWL_EXSTYLE);
        if ((exStyle & (int)User32.WindowStylesEx.WS_EX_TOPMOST) != 0)
        {
            return (null, null);
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

        var left = windowRect.Left;
        var top = windowRect.Top + windowRect.Height - clientRect.Height;
        var right = left + clientRect.Width;
        var bottom = top + clientRect.Height;

        return (region, new RECT(left, top, right, bottom));
    }

    private static IntPtr GetMonitorHandle(nint hWnd)
    {
        var monitor = User32.MonitorFromWindow(hWnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
        return monitor.IsInvalid ? IntPtr.Zero : monitor.DangerousGetHandle();
    }

    private Texture2D ProcessHdrTexture(
        SharpDX.Direct3D11.Device device,
        DeviceContext context,
        Texture2D hdrTexture)
    {
        var width = hdrTexture.Description.Width;
        var height = hdrTexture.Description.Height;

        _hdrOutputTexture ??= Direct3D11Helper.CreateOutputTexture(device, width, height);
        if (_hdrComputeShader is null)
        {
            using var shaderBytecode = ShaderBytecode.Compile(HdrToSdrShader.Content, "CS_HDRtoSDR", "cs_5_0");
            _hdrComputeShader = new ComputeShader(device, shaderBytecode);
        }

        if (_hdrParametersBuffer is null)
        {
            var parameters = new System.Numerics.Vector4(_hdrSdrWhiteScale, 0f, 0f, 0f);
            _hdrParametersBuffer = SharpDX.Direct3D11.Buffer.Create(
                device,
                BindFlags.ConstantBuffer,
                ref parameters);
        }

        using var inputSrv = new ShaderResourceView(device, hdrTexture);
        using var outputUav = new UnorderedAccessView(device, _hdrOutputTexture);

        try
        {
            context.ComputeShader.Set(_hdrComputeShader);
            context.ComputeShader.SetConstantBuffer(0, _hdrParametersBuffer);
            context.ComputeShader.SetShaderResource(0, inputSrv);
            context.ComputeShader.SetUnorderedAccessView(0, outputUav);

            var threadGroupCountX = (int)Math.Ceiling(width / 16.0);
            var threadGroupCountY = (int)Math.Ceiling(height / 16.0);
            context.Dispatch(threadGroupCountX, threadGroupCountY, 1);
        }
        finally
        {
            context.ComputeShader.SetShaderResource(0, null);
            context.ComputeShader.SetUnorderedAccessView(0, null);
            context.ComputeShader.SetConstantBuffer(0, null);
            context.ComputeShader.Set(null);
        }

        return _hdrOutputTexture;
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        _frameAccessLock.EnterWriteLock();
        try
        {
            if (_hWnd == 0)
            {
                return;
            }

            var shouldRefreshHdrDisplay = false;
            var shouldResizeFramePool = false;
            var hdrDisplayRefreshMonitor = IntPtr.Zero;
            var captureSize = default(Windows.Graphics.SizeInt32);
            using (var frame = sender.TryGetNextFrame())
            {
                if (frame == null)
                {
                    return;
                }

                if (_captureHdrRequested && Interlocked.Exchange(ref _hdrDisplayRefreshPending, 0) != 0)
                {
                    hdrDisplayRefreshMonitor = GetMonitorHandle(_hWnd);
                    shouldRefreshHdrDisplay = hdrDisplayRefreshMonitor != IntPtr.Zero &&
                                              hdrDisplayRefreshMonitor != _hdrDisplayMonitor;
                }

                if (shouldRefreshHdrDisplay)
                {
                    captureSize = _captureItem!.Size;
                }
                else if (_frameTimer.ElapsedMilliseconds - _lastFrameTime < 16)
                {
                    return;
                }
                else
                {
                    _lastFrameTime = _frameTimer.ElapsedMilliseconds;

                    captureSize = _captureItem!.Size;
                    if (captureSize.Width != _surfaceWidth || captureSize.Height != _surfaceHeight)
                    {
                        if (User32.IsIconic(_hWnd))
                        {
                            return;
                        }

                        shouldResizeFramePool = true;
                    }
                    else
                    {
                        try
                        {
                            using var surfaceTexture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
                            var d3dDevice = _sharpDxDevice!;
                            var sourceTexture = _isHdrEnabled
                                ? ProcessHdrTexture(d3dDevice, _d3dContext!, surfaceTexture)
                                : surfaceTexture;

                            _stagingTexture ??= Direct3D11Helper.CreateStagingTexture(
                                d3dDevice,
                                frame.ContentSize.Width,
                                frame.ContentSize.Height,
                                _region,
                                sourceTexture.Description.Format);
                            var newFrame = _stagingTexture.CreateMat(d3dDevice, sourceTexture, _region);

                            var oldFrame = _latestFrame;
                            _latestFrame = newFrame;
                            oldFrame?.Dispose();
                        }
                        catch (SharpDXException e)
                        {
                            Debug.WriteLine($"SharpDXException: {e.Descriptor}");
                        }
                    }
                }
            }

            if (shouldRefreshHdrDisplay)
            {
                RefreshHdrDisplayPipeline(sender, captureSize, hdrDisplayRefreshMonitor);
                return;
            }

            if (shouldResizeFramePool)
            {
                sender.Recreate(_d3dDevice, _pixelFormat, 2, captureSize);
                _stagingTexture?.Dispose();
                _stagingTexture = null;
                _hdrOutputTexture?.Dispose();
                _hdrOutputTexture = null;
                _latestFrame?.Dispose();
                _latestFrame = null;
                _surfaceWidth = captureSize.Width;
                _surfaceHeight = captureSize.Height;
                (_region, _captureRect) = GetGameScreenInfo(_hWnd);
            }
        }
        finally
        {
            _frameAccessLock.ExitWriteLock();
        }
    }

    public GameCaptureFrame? Capture()
    {
        // 使用读锁获取最新帧
        _frameAccessLock.EnterReadLock();
        try
        {
            // 返回最新帧的副本（这里我们必须克隆，因为Mat是不线程安全的）
            var frame = _latestFrame?.Clone();
            return frame == null
                ? null
                : new GameCaptureFrame(frame, _captureRect);
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
            IsCapturing = false;
            _hWnd = 0;
            _frameTimer.Reset();

            if (_captureItem != null)
            {
                _captureItem.Closed -= CaptureItemOnClosed;
            }
            if (_captureFramePool != null)
            {
                _captureFramePool.FrameArrived -= OnFrameArrived;
            }

            _captureSession?.Dispose();
            _captureSession = null;
            _captureFramePool?.Dispose();
            _captureFramePool = null;
            _captureItem = null;
            _latestFrame?.Dispose();
            _latestFrame = null;
            _captureRect = null;
            _hdrDisplayMonitor = IntPtr.Zero;
            Volatile.Write(ref _hdrDisplayRefreshPending, 0);
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _hdrOutputTexture?.Dispose();
            _hdrOutputTexture = null;
            _hdrComputeShader?.Dispose();
            _hdrComputeShader = null;
            _hdrParametersBuffer?.Dispose();
            _hdrParametersBuffer = null;
            _d3dContext?.Dispose();
            _d3dContext = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
            _sharpDxDevice?.Dispose();
            _sharpDxDevice = null;
        }
        finally
        {
            _frameAccessLock.ExitWriteLock();
        }
    }

    private void RefreshHdrDisplayPipeline(
        Direct3D11CaptureFramePool sender,
        Windows.Graphics.SizeInt32 captureSize,
        IntPtr monitor)
    {
        var displayState = HdrDisplayInformation.GetState(_hWnd);
        if (!displayState.IsKnown)
        {
            return;
        }

        var pipelineDecision = ResolveHdrPipeline(displayState);
        var pixelFormat = pipelineDecision.IsHdrEnabled
            ? DirectXPixelFormat.R16G16B16A16Float
            : DirectXPixelFormat.B8G8R8A8UIntNormalized;
        sender.Recreate(_d3dDevice!, pixelFormat, 2, captureSize);
        _isHdrEnabled = pipelineDecision.IsHdrEnabled;
        _hdrSdrWhiteScale = pipelineDecision.SdrWhiteScale;
        _pixelFormat = pixelFormat;
        _hdrDisplayMonitor = monitor;

        _stagingTexture?.Dispose();
        _stagingTexture = null;
        _hdrOutputTexture?.Dispose();
        _hdrOutputTexture = null;
        _hdrComputeShader?.Dispose();
        _hdrComputeShader = null;
        _hdrParametersBuffer?.Dispose();
        _hdrParametersBuffer = null;
        _latestFrame?.Dispose();
        _latestFrame = null;
        _surfaceWidth = captureSize.Width;
        _surfaceHeight = captureSize.Height;
        (_region, _captureRect) = GetGameScreenInfo(_hWnd);
    }

    private void CaptureItemOnClosed(GraphicsCaptureItem sender, object args)
    {
        Stop();
    }
}

internal readonly record struct HdrPipelineDecision(bool IsHdrEnabled, float SdrWhiteScale);
