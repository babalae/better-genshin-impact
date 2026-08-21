using System.Collections.Concurrent;
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
using SharpDX.DXGI;

namespace Fischless.GameCapture.Graphics;

public class GraphicsCaptureV3(bool captureHdr = false) : IGameCapture
{
    // BGR Mat 池：有界 ConcurrentQueue，FIFO + 闲置老化回收
    // V2 为 48，此版按需求改为 16（与 IdleRetain 对齐，峰值内存减半）
    private const int MaxPoolSize = 16;
    private const int IdleRetainMax = 16;
    private const int IdleTrimAcquireThreshold = 8;
    private readonly ConcurrentQueue<Mat> _bgrQueue = new();
    private bool _bgrPoolClosed = true;
    private readonly HashSet<Mat> _bgrBorrowed = new();

    private long _poolAcquireHit;
    private long _poolAcquireMissEmpty;
    private long _poolAcquireMissDisposed;
    private long _poolAcquireMissSize;
    private long _poolReleasePushed;
    private long _poolReleaseDropDisposed;
    private long _poolReleaseDropClosed;
    private long _poolReleaseDropFull;
    private long _poolAcquireTotal;
    private long _poolReleaseTotal;
    private long _poolTrimmed;
    private int _windowAcquireCount;

    private nint _hWnd;

    private Direct3D11CaptureFramePool? _captureFramePool;
    private GraphicsCaptureItem? _captureItem;
    private GraphicsCaptureSession? _captureSession;
    private IDirect3DDevice? _d3dDevice;

    public bool IsCapturing { get; private set; }

    private ResourceRegion? _region;
    private RECT? _captureRect;

    // HDR 相关
    private bool _isHdrEnabled = captureHdr;
    private DirectXPixelFormat _pixelFormat;
    private Texture2D? _hdrOutputTexture;
    private ComputeShader? _hdrComputeShader;
    private UnorderedAccessView? _hdrOutputUav;

    private readonly object _lock = new();

    // 单 GPU 广播源（参考 bgi-wgc-single-slot / _gpuFrameTexture 模式）
    // 回调只做 GPU->GPU Copy 到此纹理，消费侧 Capture() 再从它 Copy 到 staging 读回
    private Texture2D? _gpuTexture;
    private bool _frameReady;

    // staging 双缓冲（参考 obs-studio/libobs/obs-video.c NUM_TEXTURES=2）
    // 流水：Stage 写 cur，Map 读 prev，下一帧翻转
    private const int StagingCount = 2;
    private readonly Texture2D?[] _stagingTextures = new Texture2D?[2];
    private readonly bool[] _stagingValid = new bool[2];
    private int _stagingIndex;

    // Surface 大小
    private int _surfaceWidth;
    private int _surfaceHeight;

    // WinEventHook：尺寸/位置变化时才查询 get_Size
    private User32.HWINEVENTHOOK _winEventHookMoveSize;
    private User32.HWINEVENTHOOK _winEventHookLocation;
    private User32.WinEventProc _winEventProc = null!;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    private const uint WINEVENT_SKIPOWNTHREAD = 0x0001;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private volatile bool _sizeDirty = true;

    // 诊断：GPU 提交 vs Map 关系
    private int _copyCountSinceLastMap;
    private long _lastMapTime;
    private long _lastDiagTime;
    private int _mapCount5s;
    private long _mapGapSum5s;
    private int _copySum5s;

    // 帧代次 + 同帧去重缓存（SharedFrameCache）
    private long _copyGen;
    private bool _sharedReadbackEnabled = true;
    private SharedFrameCache? _sharedCache;
    private long _sharedDedupHit5s;
    private long _sharedReadback5s;
    private long _captureCall5s;

    private sealed class SharedFrameCache
    {
        public Mat Owner = null!;
        public long Gen;
        public int Refs;
    }

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

            if (settings != null)
            {
                if (settings.TryGetValue("WgcSharedReadback", out var shared) && shared is bool sb)
                    _sharedReadbackEnabled = sb;
            }

            (_region, _captureRect) = GetGameScreenInfo(hWnd);

            IsCapturing = true;

            try
            {
                _captureItem = CaptureHelper.CreateItemForWindow(_hWnd);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    $"创建 WGC 捕获器失败，hWnd=0x{_hWnd.ToInt64():X8}，可能原因：窗口句柄失效、游戏窗口被最小化/未启动、或被其他应用/系统不支持图形捕获", e);
            }

            if (_captureItem == null)
            {
                throw new InvalidOperationException("Failed to create capture item.");
            }

            _surfaceWidth = _captureItem.Size.Width;
            _surfaceHeight = _captureItem.Size.Height;

            _d3dDevice = Direct3D11Helper.CreateDevice();

            try
            {
                if (!_isHdrEnabled)
                {
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

            _winEventProc = WinEventProc;
            var winEventFlags = (User32.WINEVENT)(WINEVENT_SKIPOWNPROCESS | WINEVENT_SKIPOWNTHREAD);
            _winEventHookMoveSize = User32.SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZEEND, default, _winEventProc, 0, 0, winEventFlags);
            _winEventHookLocation = User32.SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, default, _winEventProc, 0, 0, winEventFlags);
            _sizeDirty = true;

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
            lock (_lock)
            {
                _bgrPoolClosed = false;
            }
        }
        catch
        {
            Stop();
            throw;
        }
    }

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

    private Texture2D ProcessHdrTexture(Texture2D hdrTexture)
    {
        var device = hdrTexture.Device;
        var context = device.ImmediateContext;

        var width = hdrTexture.Description.Width;
        var height = hdrTexture.Description.Height;

        _hdrOutputTexture ??= Direct3D11Helper.CreateOutputTexture(device, width, height);
        _hdrOutputUav ??= new UnorderedAccessView(device, _hdrOutputTexture);
        _hdrComputeShader ??= new ComputeShader(device, ShaderBytecode.Compile(HdrToSdrShader.Content, "CS_HDRtoSDR", "cs_5_0"));

        using var inputSrv = new ShaderResourceView(device, hdrTexture);

        context.ComputeShader.Set(_hdrComputeShader);
        context.ComputeShader.SetShaderResource(0, inputSrv);
        context.ComputeShader.SetUnorderedAccessView(0, _hdrOutputUav);

        var threadGroupCountX = (int)Math.Ceiling(width / 16.0);
        var threadGroupCountY = (int)Math.Ceiling(height / 16.0);

        context.Dispatch(threadGroupCountX, threadGroupCountY, 1);

        return _hdrOutputTexture;
    }

    private void EnsureGpuTexture(SharpDX.Direct3D11.Device device, int width, int height, ResourceRegion? region)
    {
        var w = region == null ? width : region.Value.Right - region.Value.Left;
        var h = region == null ? height : region.Value.Bottom - region.Value.Top;

        if (_gpuTexture == null ||
            _gpuTexture.Description.Width != w ||
            _gpuTexture.Description.Height != h)
        {
            _gpuTexture?.Dispose();
            _gpuTexture = new Texture2D(device, new Texture2DDescription
            {
                Width = w,
                Height = h,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                CpuAccessFlags = CpuAccessFlags.None,
                BindFlags = BindFlags.None,
                OptionFlags = ResourceOptionFlags.None,
            });
        }
    }

    private void EnsureStagingTextureLocked(SharpDX.Direct3D11.Device device, int index, int width, int height)
    {
        var tex = _stagingTextures[index];
        if (tex == null || tex.Description.Width != width || tex.Description.Height != height)
        {
            tex?.Dispose();
            _stagingTextures[index] = Direct3D11Helper.CreateStagingTexture(device, width, height, null);
            _stagingValid[index] = false;
        }
    }

    private static void HandleSharpDxError(SharpDXException e)
    {
        Debug.WriteLine($"SharpDXException: {e.Descriptor}");
        if (e.ResultCode == SharpDX.DXGI.ResultCode.DeviceRemoved || e.ResultCode == SharpDX.DXGI.ResultCode.DeviceReset)
        {
            Debug.WriteLine($"[WGC] D3D 设备丢失 ({e.ResultCode})，后续捕获可能失效，等待上层重建");
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (_hWnd == 0) return;
        using var frame = sender.TryGetNextFrame();
        if (frame == null) return;

        var now = _frameTimer.ElapsedMilliseconds;

        if (_sizeDirty || now - _lastSizeFallbackCheckMs >= SizeFallbackCheckMs)
        {
            _lastSizeFallbackCheckMs = now;
            _sizeDirty = false;
            var captureSize = _captureItem!.Size;

            if (captureSize.Width != _surfaceWidth || captureSize.Height != _surfaceHeight)
            {
                if (User32.IsIconic(_hWnd)) return;
                lock (_lock)
                {
                    _captureFramePool!.Recreate(_d3dDevice, _pixelFormat, 2, captureSize);
                    _surfaceWidth = captureSize.Width;
                    _surfaceHeight = captureSize.Height;
                    (_region, _captureRect) = GetGameScreenInfo(_hWnd);
                    var newW = _region != null ? _region.Value.Right - _region.Value.Left : captureSize.Width;
                    var newH = _region != null ? _region.Value.Bottom - _region.Value.Top : captureSize.Height;
                    TrimBgrPoolForSizeLocked(newH, newW);
                    _gpuTexture?.Dispose();
                    _gpuTexture = null;
                    for (var i = 0; i < StagingCount; i++)
                    {
                        _stagingTextures[i]?.Dispose();
                        _stagingTextures[i] = null;
                        _stagingValid[i] = false;
                    }
                    _stagingIndex = 0;
                    _frameReady = false;
                    InvalidateSharedCacheLocked();
                    _hdrOutputTexture?.Dispose();
                    _hdrOutputTexture = null;
                    _hdrOutputUav?.Dispose();
                    _hdrOutputUav = null;
                }
                return;
            }
        }

        try
        {
            using var surfaceTexture = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
            lock (_lock)
            {
                var sourceTexture = _isHdrEnabled ? ProcessHdrTexture(surfaceTexture) : surfaceTexture;
                var d3dDevice = sourceTexture.Device;
                EnsureGpuTexture(d3dDevice, frame.ContentSize.Width, frame.ContentSize.Height, _region);
                var context = d3dDevice.ImmediateContext;
                if (_region != null)
                {
                    context.CopySubresourceRegion(sourceTexture, 0, _region, _gpuTexture, 0);
                }
                else
                {
                    context.CopyResource(sourceTexture, _gpuTexture);
                }
                _copyCountSinceLastMap++;
                _copyGen++;
            }
            _frameReady = true;
        }
        catch (SharpDXException e)
        {
            HandleSharpDxError(e);
        }
    }

    public GameCaptureFrame? Capture()
    {
        if (!_frameReady) return null;

        lock (_lock)
        {
            if (_gpuTexture == null) return null;

            var gen = _copyGen;
            _captureCall5s++;
            var nowMap = _frameTimer.ElapsedMilliseconds;

            try
            {
                var cache = _sharedCache;
                if (_sharedReadbackEnabled && cache != null && cache.Gen == gen && !cache.Owner.IsDisposed)
                {
                    cache.Refs++;
                    _sharedDedupHit5s++;
                    TickDiagLocked(nowMap);
                    return new GameCaptureFrame(WgcBgrMat.CreateFrom(cache.Owner, m => ReleaseShared(m, cache)), _captureRect);
                }

                var d3dDevice = _gpuTexture.Device;
                var desc = _gpuTexture.Description;
                var rect = _captureRect;

                var stagingWidth = desc.Width;
                var stagingHeight = desc.Height;

                var curIdx = _stagingIndex;
                var prevIdx = curIdx ^ 1;

                EnsureStagingTextureLocked(d3dDevice, curIdx, stagingWidth, stagingHeight);

                var stagingCur = _stagingTextures[curIdx]!;
                var context = d3dDevice.ImmediateContext;
                // Stage 写 cur（GPU -> staging cur）
                context.CopyResource(_gpuTexture, stagingCur);
                _stagingValid[curIdx] = true;

                // 选择 Map 源：优先 prev（流水），否则回退 cur（首帧/尺寸变化后）
                Texture2D? stagingToMap;
                if (_stagingTextures[prevIdx] != null &&
                    _stagingValid[prevIdx] &&
                    _stagingTextures[prevIdx]!.Description.Width == stagingWidth &&
                    _stagingTextures[prevIdx]!.Description.Height == stagingHeight)
                {
                    stagingToMap = _stagingTextures[prevIdx];
                }
                else
                {
                    stagingToMap = stagingCur;
                }

                SharedFrameCache? newCache = null;
                Mat? sharedOwner = null;
                var mat = _sharedReadbackEnabled
                    ? stagingToMap!.CreateMat(d3dDevice, out sharedOwner, AcquireBgrMat,
                        m => { if (newCache != null) ReleaseShared(m, newCache); else ReleaseBgrMat(m); })
                    : stagingToMap!.CreateMat(d3dDevice, out _, AcquireBgrMat, ReleaseBgrMat);

                if (_sharedReadbackEnabled && mat != null && sharedOwner != null)
                {
                    RetireSharedCacheLocked();
                    newCache = new SharedFrameCache { Owner = sharedOwner, Gen = gen, Refs = 1 };
                    _sharedCache = newCache;
                    _sharedReadback5s++;
                }

                // 翻转 cur/prev 供下一帧流水
                _stagingIndex ^= 1;

                var mapGapMs = nowMap - _lastMapTime;
                _lastMapTime = nowMap;
                _copySum5s += _copyCountSinceLastMap;
                _copyCountSinceLastMap = 0;
                _mapCount5s++;
                _mapGapSum5s += mapGapMs;
                TickDiagLocked(nowMap);

                return mat == null ? null : new GameCaptureFrame(mat, rect);
            }
            catch (SharpDXException e)
            {
                HandleSharpDxError(e);
                return null;
            }
        }
    }

    private Mat AcquireBgrMat(int height, int width)
    {
        lock (_lock)
        {
            _poolAcquireTotal++;
            _windowAcquireCount++;
            while (_bgrQueue.TryDequeue(out var mat))
            {
                if (_bgrBorrowed.Add(mat))
                {
                    if (mat.IsDisposed)
                    {
                        _bgrBorrowed.Remove(mat);
                        _poolAcquireMissDisposed++;
                        continue;
                    }
                    if (mat.Rows == height && mat.Cols == width && mat.Type() == MatType.CV_8UC3)
                    {
                        _poolAcquireHit++;
                        return mat;
                    }
                    _bgrBorrowed.Remove(mat);
                    _poolAcquireMissSize++;
                    mat.Dispose();
                }
                else
                {
                    _poolAcquireMissEmpty++;
                    var fresh = new Mat(height, width, MatType.CV_8UC3);
                    _bgrBorrowed.Add(fresh);
                    return fresh;
                }
            }
            _poolAcquireMissEmpty++;
            return CreateAndRegisterBgrMat(height, width);
        }
    }

    private Mat CreateAndRegisterBgrMat(int height, int width)
    {
        var fresh = new Mat(height, width, MatType.CV_8UC3);
        _bgrBorrowed.Add(fresh);
        return fresh;
    }

    private void ReleaseBgrMat(Mat mat)
    {
        lock (_lock)
        {
            _poolReleaseTotal++;
            if (mat == null || !_bgrBorrowed.Remove(mat))
            {
                _poolReleaseDropDisposed++;
                mat?.Dispose();
                return;
            }
            if (mat.IsDisposed)
            {
                _poolReleaseDropDisposed++;
                mat.Dispose();
                return;
            }
            if (_bgrPoolClosed)
            {
                _poolReleaseDropClosed++;
                mat.Dispose();
                return;
            }
            if (_bgrQueue.Count < MaxPoolSize)
            {
                _poolReleasePushed++;
                _bgrQueue.Enqueue(mat);
            }
            else
            {
                if (_bgrQueue.TryDequeue(out var stale))
                {
                    stale.Dispose();
                }
                _poolReleaseDropFull++;
                _bgrQueue.Enqueue(mat);
            }
        }
    }

    private void ReleaseShared(Mat owner, SharedFrameCache cache)
    {
        lock (_lock)
        {
            cache.Refs--;
            if (cache.Refs <= 0 && _sharedCache != cache)
            {
                ReleaseBgrMat(owner);
            }
        }
    }

    private void RetireSharedCacheLocked()
    {
        var old = _sharedCache;
        _sharedCache = null;
        if (old == null) return;
        if (old.Refs <= 0)
        {
            ReleaseBgrMat(old.Owner);
        }
    }

    private void InvalidateSharedCacheLocked()
    {
        var c = _sharedCache;
        _sharedCache = null;
        if (c == null) return;
        if (c.Refs <= 0)
        {
            _bgrBorrowed.Remove(c.Owner);
            c.Owner.Dispose();
        }
    }

    private void TickDiagLocked(long now)
    {
        if (_lastDiagTime == 0)
        {
            _lastDiagTime = now;
            _lastMapTime = now;
            return;
        }
        if (now - _lastDiagTime < 5000) return;
        Debug.WriteLine($"[WGC Diag] 5s: Map次数={_mapCount5s} 平均间隔={_mapGapSum5s / Math.Max(1, _mapCount5s):F0}ms 平均攒获Copy={_copySum5s / Math.Max(1, _mapCount5s):F1} 总提交={_copySum5s + _mapCount5s}");
        Debug.WriteLine($"[WGC Shared] 5s: Capture调用={_captureCall5s} 命中={_sharedDedupHit5s} 读回={_sharedReadback5s} 缓存refs={_sharedCache?.Refs ?? -1}");
        Debug.WriteLine($"[WGC Pool] 5s: Hit={_poolAcquireHit} Miss(空={_poolAcquireMissEmpty} 废={_poolAcquireMissDisposed} 尺寸={_poolAcquireMissSize}) Release(Pushed={_poolReleasePushed} 废={_poolReleaseDropDisposed} 关={_poolReleaseDropClosed} 满={_poolReleaseDropFull}) 池存={_bgrQueue.Count} 在途={_poolAcquireTotal - _poolReleaseTotal}(借{_poolAcquireTotal}还{_poolReleaseTotal}) 修剪={_poolTrimmed}");
        _mapCount5s = 0;
        _mapGapSum5s = 0;
        _copySum5s = 0;
        _captureCall5s = 0;
        _sharedDedupHit5s = 0;
        _sharedReadback5s = 0;
        _lastDiagTime = now;
        TrimIdleBgrLocked();
        _windowAcquireCount = 0;
    }

    private void TrimIdleBgrLocked()
    {
        if (_windowAcquireCount >= IdleTrimAcquireThreshold) return;
        if (_bgrQueue.Count <= IdleRetainMax) return;
        while (_bgrQueue.Count > IdleRetainMax)
        {
            if (!_bgrQueue.TryDequeue(out var stale)) break;
            stale.Dispose();
            _poolTrimmed++;
        }
    }

    private void TrimBgrPoolForSizeLocked(int height, int width)
    {
        var total = _bgrQueue.Count;
        if (total == 0) return;
        var recycled = 0;
        var retained = 0;
        for (var i = 0; i < total; i++)
        {
            if (!_bgrQueue.TryDequeue(out var mat)) break;
            if (mat != null && !mat.IsDisposed && mat.Rows == height && mat.Cols == width && mat.Type() == MatType.CV_8UC3)
            {
                _bgrQueue.Enqueue(mat);
                retained++;
            }
            else
            {
                mat?.Dispose();
                recycled++;
            }
        }
        Debug.WriteLine($"[WGC Pool] 尺寸变化 {width}x{height}: 清理 {total} 个 Mat，丢弃 {recycled}（旧尺寸/废），保留 {retained} 个");
    }

    public void Stop()
    {
        lock (_lock)
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
            if (_winEventHookMoveSize != default)
            {
                User32.UnhookWinEvent(_winEventHookMoveSize);
                _winEventHookMoveSize = default;
            }
            if (_winEventHookLocation != default)
            {
                User32.UnhookWinEvent(_winEventHookLocation);
                _winEventHookLocation = default;
            }
            _sizeDirty = true;
            _captureSession?.Dispose();
            _captureSession = null;
            _captureFramePool?.Dispose();
            _captureFramePool = null;
            _captureItem = null;
            _gpuTexture?.Dispose();
            _gpuTexture = null;
            for (var i = 0; i < StagingCount; i++)
            {
                _stagingTextures[i]?.Dispose();
                _stagingTextures[i] = null;
                _stagingValid[i] = false;
            }
            _stagingIndex = 0;
            while (_bgrQueue.TryDequeue(out var pooled)) pooled.Dispose();
            _bgrPoolClosed = true;
            InvalidateSharedCacheLocked();
            _hdrOutputTexture?.Dispose();
            _hdrOutputTexture = null;
            _hdrOutputUav?.Dispose();
            _hdrOutputUav = null;
            _hdrComputeShader?.Dispose();
            _hdrComputeShader = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
            _frameReady = false;
        }
    }

    private void CaptureItemOnClosed(GraphicsCaptureItem sender, object args)
    {
        Stop();
    }

    private void WinEventProc(User32.HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != 0) return;
        if (hwnd != default && hwnd.DangerousGetHandle() == _hWnd)
        {
            _sizeDirty = true;
        }
    }

    private const int SizeFallbackCheckMs = 3000;
    private long _lastSizeFallbackCheckMs;
}
