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
    public CaptureColorMode ColorMode => _isHdrEnabled
        ? CaptureColorMode.HdrToSdr
        : CaptureColorMode.Sdr;

    private ResourceRegion? _region;
    private RECT? _captureRect;

    // HDR相关
    private bool _isHdrEnabled = captureHdr;
    private bool _isHdrDisplayEnabled;
    private float _hdrSdrWhiteScale = HdrDisplayInformation.FallbackSdrWhiteScale;
    private DirectXPixelFormat _pixelFormat;
    private Texture2D? _hdrOutputTexture;
    private ComputeShader? _hdrComputeShader;
    private SharpDX.Direct3D11.Buffer? _hdrParametersBuffer;

    // 最新帧的存储
    private Mat? _latestFrame;
    private readonly ReaderWriterLockSlim _frameAccessLock = new();
    private Exception? _lastError;

    public Exception? LastError => Volatile.Read(ref _lastError);

    // 用于获取帧数据的临时纹理和暂存资源
    private Texture2D? _stagingTexture;

    // Surface 大小
    private int _surfaceWidth;
    private int _surfaceHeight;
    private bool _screenInfoRefreshPending;

    // HDR 管线依赖窗口所在的显示器；跨屏移动后由窗口事件设置请求，并在帧回调中安全重建帧池。
    private IntPtr _hdrDisplayMonitor;
    private int _hdrDisplayRefreshPending;

    // 延迟停止必须绑定到具体失败代次；同一帧池恢复成功后，旧停止任务不得再关闭健康会话。
    private long _framePoolFailureGeneration;
    // HDR 刷新使用独立代次，使“回到原屏”只取消 HDR 刷新失败，不会误取消尺寸重建失败。
    private long _hdrDisplayFailureGeneration;

    private long _lastFrameTime;
    private int _targetFrameIntervalMs = MinimumFrameIntervalMs;

    private readonly Stopwatch _frameTimer = new();

    private const int MinimumFrameIntervalMs = 16;
    private const int MaximumFrameIntervalMs = 1000;
    public const string TargetFrameIntervalSettingName = "targetFrameIntervalMs";

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 启动当前组件或任务的处理流程。
    /// </summary>
    public void Start(nint hWnd, Dictionary<string, object>? settings = null)
    {
        Stop();
        _frameAccessLock.EnterWriteLock();
        try
        {
            _hWnd = hWnd;
            (_region, _captureRect) = GetGameScreenInfo(hWnd);
            _screenInfoRefreshPending = false;
            _hdrDisplayMonitor = IntPtr.Zero;
            Volatile.Write(ref _hdrDisplayRefreshPending, 0);
            _targetFrameIntervalMs = ResolveTargetFrameInterval(settings);

            if (_captureHdrRequested)
            {
                var displayState = HdrDisplayInformation.GetState(hWnd);
                var pipelineDecision = ResolveHdrPipeline(displayState);
                _isHdrDisplayEnabled = pipelineDecision.IsHdrEnabled;
                _hdrSdrWhiteScale = pipelineDecision.SdrWhiteScale;
                _hdrDisplayMonitor = GetMonitorHandle(hWnd);
                if (_hdrDisplayMonitor == IntPtr.Zero)
                {
                    throw new InvalidOperationException("无法确定 HDR 捕获目标显示器。请检查显示器连接和显卡驱动后重试。");
                }
            }
            else
            {
                _isHdrDisplayEnabled = false;
                _hdrSdrWhiteScale = 1f;
            }
            _isHdrEnabled = _captureHdrRequested && _isHdrDisplayEnabled;

            _captureItem = CaptureHelper.CreateItemForWindow(_hWnd);

            if (_captureItem == null)
            {
                throw new InvalidOperationException("Failed to create capture item.");
            }

            _surfaceWidth = _captureItem.Size.Width;
            _surfaceHeight = _captureItem.Size.Height;

            // CreateFreeThreaded 会在内部工作线程回调；每个会话必须独占 device/context，避免多实例并发访问 immediate context。
            _d3dDevice = Direct3D11Helper.CreateDevice(out _sharpDxDevice);
            _d3dContext = _sharpDxDevice.ImmediateContext;

            // 仅在目标显示器实际启用 HDR 时创建 FP16 管线。
            if (_isHdrEnabled)
            {
                try
                {
                    _pixelFormat = DirectXPixelFormat.R16G16B16A16Float;
                    // 将色彩转换和 GPU readback 移出 WPF UI 调度线程，降低界面与输入卡顿。
                    _captureFramePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                        _d3dDevice,
                        _pixelFormat,
                        2,
                        _captureItem.Size);
                }
                catch (Exception e)
                {
                    throw new NotSupportedException("无法创建 Windows Graphics Capture HDR 帧池。", e);
                }
            }

            if (!_isHdrEnabled)
            {
                _pixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
                _captureFramePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _d3dDevice,
                    _pixelFormat,
                    2,
                    _captureItem.Size);
            }

            var captureFramePool = _captureFramePool ??
                                   throw new InvalidOperationException("Failed to create capture frame pool.");
            _captureItem.Closed += CaptureItemOnClosed;
            captureFramePool.FrameArrived += OnFrameArrived;

            _captureSession = captureFramePool.CreateCaptureSession(_captureItem);
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
            StopCore();
            throw;
        }
        finally
        {
            _frameAccessLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 解析并返回 <c>ResolveTargetFrameInterval</c> 对应的结果。
    /// </summary>
    internal static int ResolveTargetFrameInterval(Dictionary<string, object>? settings)
    {
        if (settings?.TryGetValue(TargetFrameIntervalSettingName, out var value) != true)
        {
            return MinimumFrameIntervalMs;
        }

        var requestedInterval = value switch
        {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            double doubleValue when double.IsFinite(doubleValue) => (int)Math.Round(doubleValue),
            _ => MinimumFrameIntervalMs
        };

        return Math.Clamp(requestedInterval, MinimumFrameIntervalMs, MaximumFrameIntervalMs);
    }

    /// <summary>
    /// 解析并返回 <c>ResolveHdrPipeline</c> 对应的结果。
    /// </summary>
    internal static HdrPipelineDecision ResolveHdrPipeline(HdrDisplayState displayState)
    {
        return displayState.Kind switch
        {
            HdrDisplayStateKind.Sdr => new HdrPipelineDecision(false, 1f),
            HdrDisplayStateKind.Hdr => new HdrPipelineDecision(true, displayState.SdrWhiteScale),
            HdrDisplayStateKind.HdrWhiteLevelUnavailable =>
                new HdrPipelineDecision(true, HdrDisplayInformation.FallbackSdrWhiteScale),
            _ => throw new InvalidOperationException(
                "无法确认目标窗口所在显示器的 HDR 状态，已停止启动 WindowsGraphicsCapture（HDR），" +
                "以避免按 SDR 错误捕获 HDR 画面。请检查显示器连接和显卡驱动后重试。"),
        };
    }

    /// <summary>
    /// 判断位置通知是否需要刷新 HDR 捕获管线。
    /// </summary>
    /// <param name="currentMonitor">当前管线绑定的显示器。</param>
    /// <param name="requestedMonitor">帧回调查询到的目标显示器。</param>
    /// <param name="lastError">尚未由成功帧清除的捕获错误。</param>
    /// <returns>显示器变化或旧管线仍处于故障状态时为 <see langword="true"/>。</returns>
    internal static bool ShouldRefreshHdrDisplayPipeline(
        IntPtr currentMonitor,
        IntPtr requestedMonitor,
        Exception? lastError)
    {
        return requestedMonitor != currentMonitor || lastError is not null;
    }

    /// <summary>
    /// 通知捕获器窗口位置发生变化。此方法只设置原子标记；显示器查询和帧池重建均延后到
    /// free-threaded 帧回调，避免阻塞 WinEventHook 线程或 WPF/UI 线程。
    /// </summary>
    public void NotifyWindowLocationChanged(nint hWnd)
    {
        if (!_captureHdrRequested || !IsCapturing || hWnd == 0 || hWnd != _hWnd)
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

    /// <summary>
    /// 获取 <c>GetMonitorHandle</c> 对应的数据。
    /// </summary>
    private static IntPtr GetMonitorHandle(nint hWnd)
    {
        if (hWnd == 0)
        {
            return IntPtr.Zero;
        }

        var monitor = User32.MonitorFromWindow(hWnd, User32.MonitorFlags.MONITOR_DEFAULTTONEAREST);
        return monitor.IsInvalid ? IntPtr.Zero : monitor.DangerousGetHandle();
    }

    /// <summary>
    /// 处理 <c>ProcessHdrTexture</c> 对应的数据。
    /// </summary>
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

    /// <summary>
    /// 处理 <c>OnFrameArrived</c> 对应的事件或状态更新。
    /// </summary>
    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        // 使用写锁更新最新帧
        _frameAccessLock.EnterWriteLock();
        try
        {
            // Stop/Start 后旧帧池仍可能有已排队回调；必须按 sender 隔离会话，避免 ABA 误用新资源。
            if (_hWnd == 0 || !ReferenceEquals(sender, _captureFramePool))
            {
                return;
            }

            var shouldRecreateFramePool = false;
            var shouldRefreshHdrDisplay = false;
            var hdrDisplayRefreshMonitor = IntPtr.Zero;
            var captureSize = default(Windows.Graphics.SizeInt32);
            using (var frame = sender.TryGetNextFrame())
            {
                if (frame == null)
                {
                    return;
                }

                // 在帧回调中消费位置通知，确保 WinEventHook 只做无阻塞标记。
                // Exchange 先清除本轮请求；重建期间到达的新通知会保留到下一帧。
                if (_captureHdrRequested && Interlocked.Exchange(ref _hdrDisplayRefreshPending, 0) != 0)
                {
                    try
                    {
                        hdrDisplayRefreshMonitor = GetMonitorHandle(_hWnd);
                    }
                    catch (Exception e)
                    {
                        RecordCaptureFailure(e);
                        ScheduleStopAfterFramePoolFailure(sender, isHdrDisplayFailure: true);
                        return;
                    }

                    if (hdrDisplayRefreshMonitor == IntPtr.Zero)
                    {
                        RecordCaptureFailure(new InvalidOperationException("无法确定跨屏移动后的 HDR 捕获目标。"));
                        ScheduleStopAfterFramePoolFailure(sender, isHdrDisplayFailure: true);
                        return;
                    }

                    // 同一显示器内的普通移动无需重建。只有显示器实际变化，或此前 HDR 刷新失败尚未恢复时才处理。
                    shouldRefreshHdrDisplay = ShouldRefreshHdrDisplayPipeline(
                        Volatile.Read(ref _hdrDisplayMonitor),
                        hdrDisplayRefreshMonitor,
                        Volatile.Read(ref _lastError));
                }

                // 显示器切换必须优先于节流处理，否则低采样频率下可能长时间沿用旧 HDR 管线。
                if (shouldRefreshHdrDisplay)
                {
                    captureSize = _captureItem!.Size;
                }
                else if (_frameTimer.ElapsedMilliseconds - _lastFrameTime < _targetFrameIntervalMs)
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

                        shouldRecreateFramePool = true;
                    }
                    else
                    {
                        if (_screenInfoRefreshPending && !TryRefreshGameScreenInfo())
                        {
                            // 帧池仍然有效，只跳过本帧并在下一帧继续查询窗口区域。
                            return;
                        }

                        try
                        {
                            // 从捕获的帧创建一个可以被访问的纹理
                            using var surfaceTexture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
                            var d3dDevice = _sharpDxDevice ??
                                            throw new InvalidOperationException("D3D device is unavailable.");
                            var d3dContext = _d3dContext ??
                                             throw new InvalidOperationException("D3D context is unavailable.");
                            var sourceTexture = _isHdrEnabled
                                ? ProcessHdrTexture(d3dDevice, d3dContext, surfaceTexture)
                                : surfaceTexture;

                            _stagingTexture ??= Direct3D11Helper.CreateStagingTexture(
                                d3dDevice,
                                frame.ContentSize.Width,
                                frame.ContentSize.Height,
                                _region,
                                sourceTexture.Description.Format);
                            var newFrame = _stagingTexture.CreateMat(
                                d3dContext,
                                sourceTexture,
                                _region,
                                RecordCaptureFailure);

                            // 新帧构造成功后再替换，异常时保留上一帧
                            if (newFrame is not null)
                            {
                                var oldFrame = _latestFrame;
                                _latestFrame = newFrame;
                                oldFrame?.Dispose();
                                Volatile.Write(ref _lastError, null);
                            }
                        }
                        catch (Exception e)
                        {
                            RecordCaptureFailure(e);
                        }
                    }
                }
            }

            // frame 已经释放后才能调用 Recreate；切换显示器时同时刷新像素格式、白电平和 GPU 资源。
            if (shouldRefreshHdrDisplay)
            {
                if (!TryRefreshHdrDisplayPipeline(sender, captureSize, hdrDisplayRefreshMonitor))
                {
                    return;
                }

                return;
            }

            // 必须先释放并归还当前 frame，再重建帧池。
            if (shouldRecreateFramePool)
            {
                if (!ReferenceEquals(sender, _captureFramePool) ||
                    captureSize.Width <= 0 || captureSize.Height <= 0)
                {
                    RecordCaptureFailure(new InvalidOperationException(
                        $"Capture frame pool received an invalid resize request: {captureSize.Width}x{captureSize.Height}."));
                    ScheduleStopAfterFramePoolFailure(sender);
                    return;
                }

                try
                {
                    sender.Recreate(
                        _d3dDevice,
                        _pixelFormat,
                        2,
                        captureSize);
                    // Recreate 成功代表当前帧池已恢复，令此前排队的延迟停止请求失效。
                    // 仅恢复尺寸帧池，不代表 HDR 显示器管线已经匹配；保留 HDR 刷新失败请求。
                    InvalidateFramePoolFailureGeneration();
                }
                catch (Exception e)
                {
                    RecordCaptureFailure(e);
                    // 当前回调持有写锁，不能同步 Stop；排队到回调退出后再安全释放当前会话。
                    ScheduleStopAfterFramePoolFailure(sender);
                    return;
                }

                // Recreate 已成功，后续窗口查询失败不能再把健康帧池当作损坏会话停止。
                _stagingTexture?.Dispose();
                _stagingTexture = null;
                _hdrOutputTexture?.Dispose();
                _hdrOutputTexture = null;
                // 旧帧尺寸/区域已失效；刷新成功前不向识别层暴露过期画面。
                _latestFrame?.Dispose();
                _latestFrame = null;
                _surfaceWidth = captureSize.Width;
                _surfaceHeight = captureSize.Height;
                _screenInfoRefreshPending = !TryRefreshGameScreenInfo();
                // 尺寸变化帧已经消耗了本轮节流配额；回退时间戳，让新尺寸的下一帧立即恢复截图。
                _lastFrameTime = _frameTimer.ElapsedMilliseconds - _targetFrameIntervalMs;
            }
        }
        finally
        {
            _frameAccessLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 捕获并返回 <c>Capture</c> 对应的画面数据。
    /// </summary>
    public GameCaptureFrame? Capture()
    {
        // 使用读锁获取最新帧
        _frameAccessLock.EnterReadLock();
        try
        {
            // GPU/readback 出错后不能继续向识别层返回冻结的旧画面；成功产出新帧时会清除 LastError。
            if (_lastError is not null)
            {
                return null;
            }

            // 返回最新帧的副本（这里我们必须克隆，因为Mat是不线程安全的）
            var frame = _latestFrame?.Clone();
            return frame == null
                ? null
                : new GameCaptureFrame(frame, _captureRect, ColorMode);
        }
        finally
        {
            _frameAccessLock.ExitReadLock();
        }
    }

    /// <summary>
    /// 停止或重置 <c>Stop</c> 对应的状态。
    /// </summary>
    public void Stop()
    {
        _frameAccessLock.EnterWriteLock();
        try
        {
            StopCore();
        }
        finally
        {
            _frameAccessLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// 尝试执行 <c>TryRefreshHdrDisplayPipeline</c> 对应的操作。
    /// </summary>
    private bool TryRefreshHdrDisplayPipeline(
        Direct3D11CaptureFramePool sender,
        Windows.Graphics.SizeInt32 captureSize,
        IntPtr monitor)
    {
        if (monitor == IntPtr.Zero || captureSize.Width <= 0 || captureSize.Height <= 0)
        {
            RecordCaptureFailure(new InvalidOperationException("无法确定跨屏移动后的 HDR 捕获目标。"));
            ScheduleStopAfterFramePoolFailure(sender, isHdrDisplayFailure: true);
            return false;
        }

        // 窗口可能在多个位置事件之间移回原显示器，此时需要验证原管线是否仍可用。
        if (monitor == Volatile.Read(ref _hdrDisplayMonitor))
        {
            // 不能只清除 pending：此前 Recreate 失败可能令帧池或 LastError 仍处于故障状态。
            // 先用原管线重新 Recreate，成功后才取消 HDR 延迟停止并恢复下一帧输出。
            return TryRecoverHdrPipelineOnOriginalDisplay(sender, captureSize);
        }

        HdrPipelineDecision pipelineDecision;
        try
        {
            pipelineDecision = ResolveHdrPipeline(HdrDisplayInformation.GetState(_hWnd));
        }
        catch (Exception e)
        {
            // 未能确认新显示器状态时不能沿用旧管线，否则会把 SDR 当 HDR（或反之）交给识别层。
            RecordCaptureFailure(e);
            ScheduleStopAfterFramePoolFailure(sender, isHdrDisplayFailure: true);
            return false;
        }

        var pixelFormat = pipelineDecision.IsHdrEnabled
            ? DirectXPixelFormat.R16G16B16A16Float
            : DirectXPixelFormat.B8G8R8A8UIntNormalized;
        var d3dDevice = _d3dDevice;
        if (d3dDevice is null)
        {
            RecordCaptureFailure(new InvalidOperationException("D3D device is unavailable during HDR display refresh."));
            ScheduleStopAfterFramePoolFailure(sender, isHdrDisplayFailure: true);
            return false;
        }

        try
        {
            sender.Recreate(d3dDevice, pixelFormat, 2, captureSize);
            // 成功切换管线同样视为帧池恢复，不能让旧失败任务误停新管线。
            InvalidateFramePoolFailureGenerations();
        }
        catch (Exception e)
        {
            RecordCaptureFailure(e);
            ScheduleStopAfterFramePoolFailure(sender, isHdrDisplayFailure: true);
            return false;
        }

        _isHdrDisplayEnabled = pipelineDecision.IsHdrEnabled;
        _isHdrEnabled = _captureHdrRequested && pipelineDecision.IsHdrEnabled;
        _hdrSdrWhiteScale = pipelineDecision.SdrWhiteScale;
        _pixelFormat = pixelFormat;
        _hdrDisplayMonitor = monitor;

        try
        {
            // Recreate 后旧 staging/output 纹理的格式可能不再匹配；全部清理并让下一帧按新管线懒加载。
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
        }
        catch (Exception e)
        {
            // 即使资源释放失败也不能让 free-threaded 回调异常逃逸；交由统一故障路径安全停止会话。
            RecordCaptureFailure(e);
            ScheduleStopAfterFramePoolFailure(sender, isHdrDisplayFailure: true);
            return false;
        }

        _surfaceWidth = captureSize.Width;
        _surfaceHeight = captureSize.Height;
        _screenInfoRefreshPending = !TryRefreshGameScreenInfo();
        _lastFrameTime = _frameTimer.ElapsedMilliseconds - _targetFrameIntervalMs;
        return true;
    }

    /// <summary>
    /// 尝试执行 <c>TryRecoverHdrPipelineOnOriginalDisplay</c> 对应的操作。
    /// </summary>
    private bool TryRecoverHdrPipelineOnOriginalDisplay(
        Direct3D11CaptureFramePool sender,
        Windows.Graphics.SizeInt32 captureSize)
    {
        var d3dDevice = _d3dDevice;
        if (d3dDevice is null)
        {
            RecordCaptureFailure(new InvalidOperationException("D3D device is unavailable while recovering HDR capture."));
            ScheduleStopAfterFramePoolFailure(sender, isHdrDisplayFailure: true);
            return false;
        }

        try
        {
            // 回到原显示器后仍使用当前已知的像素格式；Recreate 成功才说明帧池确实恢复。
            sender.Recreate(d3dDevice, _pixelFormat, 2, captureSize);
            InvalidateFramePoolFailureGenerations();

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
            _screenInfoRefreshPending = !TryRefreshGameScreenInfo();
            _lastFrameTime = _frameTimer.ElapsedMilliseconds - _targetFrameIntervalMs;

            // 仅在窗口区域查询也成功时清除之前的 HDR 错误；否则保留诊断并让下一帧继续刷新区域。
            if (!_screenInfoRefreshPending)
            {
                Volatile.Write(ref _lastError, null);
            }

            return true;
        }
        catch (Exception e)
        {
            RecordCaptureFailure(e);
            ScheduleStopAfterFramePoolFailure(sender, isHdrDisplayFailure: true);
            return false;
        }
    }

    /// <summary>
    /// 停止或重置 <c>StopCore</c> 对应的状态。
    /// </summary>
    private void StopCore(bool preserveLastError = false)
    {
        // 会话停止/重启会产生新的生命周期，所有旧帧池失败任务都必须失效。
        InvalidateFramePoolFailureGenerations();
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
        if (!preserveLastError)
        {
            Volatile.Write(ref _lastError, null);
        }
        _captureRect = null;
        _screenInfoRefreshPending = false;
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

    /// <summary>
    /// 尝试执行 <c>TryRefreshGameScreenInfo</c> 对应的操作。
    /// </summary>
    private bool TryRefreshGameScreenInfo()
    {
        try
        {
            (_region, _captureRect) = GetGameScreenInfo(_hWnd);
            _screenInfoRefreshPending = false;
            return true;
        }
        catch (Exception e)
        {
            RecordCaptureFailure(e);
            return false;
        }
    }

    /// <summary>
    /// 记录 <c>RecordCaptureFailure</c> 对应的状态。
    /// </summary>
    private void RecordCaptureFailure(Exception exception)
    {
        Volatile.Write(ref _lastError, exception);
        // Release 日志由上层统一限流输出，避免在帧回调持锁期间逐帧格式化完整异常。
        Debug.WriteLine($"Graphics capture failed: {exception}");
    }

    /// <summary>
    /// 使 <c>InvalidateFramePoolFailureGeneration</c> 对应的旧状态失效。
    /// </summary>
    private void InvalidateFramePoolFailureGeneration()
    {
        Interlocked.Increment(ref _framePoolFailureGeneration);
    }

    /// <summary>
    /// 使 <c>InvalidateHdrDisplayFailureGeneration</c> 对应的旧状态失效。
    /// </summary>
    private void InvalidateHdrDisplayFailureGeneration()
    {
        Interlocked.Increment(ref _hdrDisplayFailureGeneration);
    }

    /// <summary>
    /// 使 <c>InvalidateFramePoolFailureGenerations</c> 对应的旧状态失效。
    /// </summary>
    private void InvalidateFramePoolFailureGenerations()
    {
        InvalidateFramePoolFailureGeneration();
        InvalidateHdrDisplayFailureGeneration();
    }

    /// <summary>
    /// 安排 <c>ScheduleStopAfterFramePoolFailure</c> 对应的延迟操作。
    /// </summary>
    private void ScheduleStopAfterFramePoolFailure(
        Direct3D11CaptureFramePool failedFramePool,
        bool isHdrDisplayFailure = false)
    {
        // 记录失败代次而不只比较帧池引用；同一帧池可能在异步停止执行前已经成功 Recreate。
        var failureGeneration = isHdrDisplayFailure
            ? Interlocked.Increment(ref _hdrDisplayFailureGeneration)
            : Interlocked.Increment(ref _framePoolFailureGeneration);

        _ = Task.Run(() =>
        {
            var lockTaken = false;
            try
            {
                _frameAccessLock.EnterWriteLock();
                lockTaken = true;
                // 重启期间可能已有新会话，只清理实际失败的旧帧池。
                var currentFailureGeneration = isHdrDisplayFailure
                    ? Volatile.Read(ref _hdrDisplayFailureGeneration)
                    : Volatile.Read(ref _framePoolFailureGeneration);
                if (ReferenceEquals(failedFramePool, _captureFramePool) &&
                    currentFailureGeneration == failureGeneration)
                {
                    // 保留触发停止的 D3D/帧池异常，供上层日志准确说明会话为何失效。
                    StopCore(preserveLastError: true);
                }
            }
            catch (Exception e)
            {
                RecordCaptureFailure(e);
            }
            finally
            {
                if (lockTaken)
                {
                    _frameAccessLock.ExitWriteLock();
                }
            }
        });
    }

    /// <summary>
    /// 捕获并返回 <c>CaptureItemOnClosed</c> 对应的画面数据。
    /// </summary>
    private void CaptureItemOnClosed(GraphicsCaptureItem sender, object args)
    {
        _frameAccessLock.EnterWriteLock();
        try
        {
            if (ReferenceEquals(sender, _captureItem))
            {
                // 只允许当前会话的 Closed 事件拆除资源，忽略重启前已排队的旧事件。
                StopCore();
            }
        }
        finally
        {
            _frameAccessLock.ExitWriteLock();
        }
    }
}

internal readonly record struct HdrPipelineDecision(bool IsHdrEnabled, float SdrWhiteScale);
