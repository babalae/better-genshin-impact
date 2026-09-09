using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

// 部分设计参考 FFmpeg 的 WGC 捕获实现（libavfilter/vsrc_gfxcapture_winrt.cpp，LGPL-2.1-or-later）

namespace Fischless.GameCapture.Graphics;

public class GraphicsCaptureV2(bool captureHdr = false) : IGameCapture
{
    // BGR Mat 池：有界 ConcurrentQueue，FIFO 回收
    private const int MaxPoolSize = 16;
    private readonly ConcurrentQueue<Mat> _bgrQueue = new();
    private bool _bgrPoolClosed = true;
    private readonly HashSet<Mat> _bgrBorrowed = new();

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

    // FFmpeg gfxcapture 式限流：MinUpdateInterval 把 DWM 推帧率限到消费需求率（26100+，接口 QI 调用，见 TryApplyMinUpdateInterval）
    private Windows.Graphics.SizeInt32 _capSize;
    private long _minUpdateIntervalHns = 500_000L;   // 默认 50ms（≈20fps），WinRT TimeSpan（100ns 单位）；0 = 不启用限流

    // IGraphicsCaptureSession5 {67C0EA62-1F85-5061-925A-239BE0AC09CB}
    private static readonly Guid GraphicsCaptureSession5Iid = new("67C0EA62-1F85-5061-925A-239BE0AC09CB");

    // vtable 布局：IUnknown 0-2 + IInspectable 3-5 + get_MinUpdateInterval 6 + put_MinUpdateInterval 7
    private const int PutMinUpdateIntervalVtableSlot = 7;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PutMinUpdateIntervalDelegate(IntPtr pThis, long duration);

    // BGR24 打包四件套见上方字段；消费被 _captureGate 串行化，单块 staging buffer 即安全

    // 单飞闸门：并发消费方逐个进入
    private readonly object _captureGate = new();

    // 在途捕获数（正持锁外 Map/读回）。Stop 与尺寸 Recreate 销毁 staging 前必须等其归零；仅锁内访问
    private int _activeCaptures;

    // 无新帧跳过读回：消费时 TryGetNextFrame 为空（池已排空，无新内容），克隆缓存直接返回，
    // 避免对旧内容重复 GPU/PCIe 回读空转。缓存常建：每次消费新帧时写入缓存 + 克隆给消费者
    private Mat? _cachedBgr;
    private long _cachedFrameTs;         // 缓存内容对应的帧时间戳

    // BGR24 打包（GPU 完成颜色转换与打包，CPU 免转换；回读量 8.3→6.2MB -25%）
    private ComputeShader? _packCs;
    private SharpDX.Direct3D11.Buffer? _packCb;           // uint4 dims（W,H,total,0），Dynamic
    private SharpDX.Direct3D11.Buffer? _packGpuBuf;       // 打包结果（GPU），StructuredBuffer<uint>
    private UnorderedAccessView? _packUav;
    private SharpDX.Direct3D11.Buffer? _packStagingBuf;   // CPU 可读副本（Staging）

    // 注意：SharpDX Compile(string) 用 StringToHGlobalAnsi + 字符数当字节数，非 ASCII 字符
    // （如中文注释）会把源码尾部截断导致 FXC 编译失败——此字符串内必须保持纯 ASCII
    private const string PackShaderSrc = @"
        cbuffer cb : register(b0) { uint4 dims; }   // x=W y=H z=total
        Texture2D<float4> src : register(t0);
        RWStructuredBuffer<uint> outBuf : register(u0);
        [numthreads(64,1,1)]
        void main_cs(uint3 dt : SV_DispatchThreadID)
        {
            uint p = dt.x * 4;                       // 4 pixels/thread = 12 bytes = 3 aligned dwords
            if (p >= dims.z) return;
            uint bc[4]; uint gc[4]; uint rc[4];
            [unroll]
            for (uint k = 0; k < 4; ++k)
            {
                uint idx = p + k;
                uint x = idx % dims.x;
                uint y = idx / dims.x;
                float4 px = src.Load(int3(x, y, 0)); // B8G8R8A8 auto-swizzled: px.x=R px.y=G px.z=B
                bc[k] = (uint)(saturate(px.z) * 255.0f + 0.5f);
                gc[k] = (uint)(saturate(px.y) * 255.0f + 0.5f);
                rc[k] = (uint)(saturate(px.x) * 255.0f + 0.5f);
            }
            uint o = p / 4 * 3;                      // group base (bytes) -> dword index, 4-byte aligned
            outBuf[o + 0] = bc[0] | (gc[0] << 8)  | (rc[0] << 16) | (bc[1] << 24);
            outBuf[o + 1] = gc[1] | (rc[1] << 8)  | (bc[2] << 16) | (gc[2] << 24);
            outBuf[o + 2] = rc[2] | (bc[3] << 8)  | (gc[3] << 16) | (rc[3] << 24);
        }
    ";

    // 单 GPU 广播源：FrameArrived（DWM 写完、所有权刚释放的时机）用像素着色器一次 draw 渲染写入
    // （FFmpeg gfxcapture 同款）。实测：消费时刻直接从跨进程表面回读会固定等待一个 DWM 周期（~20ms/帧），
    // 到达时写入则无此等待
    private Texture2D? _gpuTexture;
    private RenderTargetView? _gpuTextureRtv;
    private long _gpuFrameTs;            // _gpuTexture 当前内容对应帧的时间戳（_lock 内写）

    // draw 写入资源（FFmpeg gfxcapture 的全屏三角形 + 采样，客户区裁剪折进 UV 窗口）
    private VertexShader? _drawVs;
    private PixelShader? _drawPs;
    private SamplerState? _drawSampler;
    private SharpDX.Direct3D11.Buffer? _drawCb;

    private const string DrawShaderSrc = @"
        cbuffer cb : register(b0) { float4 uvWindow; }
        Texture2D t0 : register(t0);
        SamplerState s0 : register(s0);
        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
        VSOut main_vs(uint id : SV_VertexID) {
            VSOut o;
            o.pos = float4(id == 2 ? 3.0 : -1.0, id == 1 ? 3.0 : -1.0, 0, 1);
            o.uv = lerp(uvWindow.xy, uvWindow.zw, float2((o.pos.x + 1) * 0.5, 1 - (o.pos.y + 1) * 0.5));
            return o;
        }
        float4 main_ps(VSOut i) : SV_Target { return t0.Sample(s0, i.uv); }
    ";

    // A/B 开关：true = FFmpeg 式 draw 写入；false = 旧版 CopySubresourceRegion 拷贝写入
    private bool _useDrawWrite = true;

    // A/B 开关（settings 键 UseCpuConvert）：true = 旧 BGRA staging + CPU CvtColor；false = GPU 打包 BGR24
    private bool _useCpuConvert;

    // CPU 转换回退路径（_useCpuConvert=true 时启用）：BGRA 整帧 staging，Map 后 CvtColor
    private Texture2D? _stagingTexture;

    // 诊断计数（仅 _captureGate 内访问）：定位刷新/占用问题时用 DebugView 查看
    private long _diagTicks;
    private long _diagFrames;
    private long _diagCacheHits;
    private long _diagRecreates;
    private double _sumPackMs;
    private double _sumCopyMs;
    private double _sumStageMs;
    private double _sumCvtMs;
    private double _sumMapMs;
    private double _sumCloneMs;

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    public void Start(nint hWnd, Dictionary<string, object>? settings = null)
    {
            // 全程持锁，防并发双 Start 泄漏 framepool/session（Monitor 可重入，内部 Stop 安全）
        lock (_lock)
        {
            StartCore(hWnd, settings);
        }
    }

    private void StartCore(nint hWnd, Dictionary<string, object>? settings = null)
    {
        Stop();
        try
        {
            _hWnd = hWnd;

            // 识别节拍可通过 settings 传入（键 MinUpdateIntervalMs，毫秒）；0 = 不启用限流
            if (settings?.TryGetValue("MinUpdateIntervalMs", out var intervalObj) == true)
            {
                try
                {
                    var ms = Convert.ToInt64(intervalObj);
                    _minUpdateIntervalHns = ms > 0 ? ms * 10_000L : 0;
                }
                catch
                {
                    // 非法值忽略，保持默认
                }
            }

            // 到达侧写入方式开关（A/B 对比用）
            if (settings?.TryGetValue("DisableDrawWrite", out var disableDrawWriteObj) == true)
            {
                try
                {
                    _useDrawWrite = !Convert.ToBoolean(disableDrawWriteObj);
                }
                catch
                {
                    // 非法值忽略
                }
            }

            // 颜色转换开关（A/B 对比/回退用）：true = 旧 BGRA staging + CPU CvtColor；false = GPU 打包 BGR24
            if (settings?.TryGetValue("UseCpuConvert", out var useCpuConvertObj) == true)
            {
                try
                {
                    _useCpuConvert = Convert.ToBoolean(useCpuConvertObj);
                }
                catch
                {
                    // 非法值忽略
                }
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

            _capSize = _captureItem.Size;

            _d3dDevice = Direct3D11Helper.CreateDevice();

            // CreateFreeThreaded 契约：帧池对象可跨线程访问，不依赖 DispatcherQueue/消息泵；
            // TryGetNextFrame/Recreate 由消费线程在 _lock 内调用（FFmpeg gfxcapture 同款需求驱动模型）
            try
            {
                if (!_isHdrEnabled)
                {
                    throw new Exception();
                }

                _pixelFormat = DirectXPixelFormat.R16G16B16A16Float;
                _captureFramePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    _d3dDevice,
                    _pixelFormat,
                    2,
                    _captureItem.Size);
            }
            catch (Exception)
            {
                _pixelFormat = DirectXPixelFormat.B8G8R8A8UIntNormalized;
                _captureFramePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
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

            // FFmpeg gfxcapture 同款：StartCapture 前设置最小更新间隔，把推帧率限到消费需求率
            TryApplyMinUpdateInterval(_captureSession);

            _captureSession.StartCapture();
            Debug.WriteLine($"[WGC V2] Start 完成: hdr={_isHdrEnabled}, fmt={_pixelFormat}, minUpdateInterval={(_minUpdateIntervalHns > 0 ? $"{_minUpdateIntervalHns / 10000.0:0.##}ms" : "off")}, capSize={_capSize.Width}x{_capSize.Height}");
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

        // V2 消费侧需要从输出纹理读原始字节打包：UNORM 资源 + 同格式显式视图（浮点往返精确还原字节）
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

        // 解绑，避免后续 CopyResource 触发 read/write hazard
        context.ComputeShader.SetUnorderedAccessView(0, null);
        context.ComputeShader.SetShaderResource(0, null);
        context.ComputeShader.Set(null);

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
            _gpuTextureRtv?.Dispose();
            _gpuTextureRtv = null;
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
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                OptionFlags = ResourceOptionFlags.None,
            });
            _gpuTextureRtv = new RenderTargetView(device, _gpuTexture);
        }
    }

    private void EnsureDrawResources(SharpDX.Direct3D11.Device device)
    {
        if (_drawVs != null) return;

        using var vsBlob = ShaderBytecode.Compile(DrawShaderSrc, "main_vs", "vs_5_0");
        using var psBlob = ShaderBytecode.Compile(DrawShaderSrc, "main_ps", "ps_5_0");
        _drawVs = new VertexShader(device, vsBlob);
        _drawPs = new PixelShader(device, psBlob);
        _drawSampler = new SamplerState(device, new SamplerStateDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            // 结构体默认值 0 对 D3D11_COMPARISON_FUNC 是非法枚举，必须显式设置（否则 CreateSamplerState E_INVALIDARG）
            ComparisonFunction = Comparison.Never,
            MinimumLod = 0,
            MaximumLod = float.MaxValue,
        });
        _drawCb = new SharpDX.Direct3D11.Buffer(device, 16, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
    }

    private void EnsurePackResourcesLocked(SharpDX.Direct3D11.Device device, int width, int height)
    {
        var bytes = ((width * height + 3) / 4) * 12; // 每 4 像素由 shader 固定写入 3 个 dword，按组取整避免末组 UAV 越界
        if (_packGpuBuf != null && _packGpuBuf.Description.SizeInBytes == bytes) return;

        _packUav?.Dispose();
        _packGpuBuf?.Dispose();
        _packStagingBuf?.Dispose();

        _packCs ??= new ComputeShader(device, ShaderBytecode.Compile(PackShaderSrc, "main_cs", "cs_5_0"));
        _packCb ??= new SharpDX.Direct3D11.Buffer(device, 16, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write, ResourceOptionFlags.None, 0);
        _packGpuBuf = new SharpDX.Direct3D11.Buffer(device, bytes, ResourceUsage.Default, BindFlags.UnorderedAccess, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4);
        // structured buffer 的 UAV 描述必须显式给出（Format=Unknown），否则驱动可能拒绝
        _packUav = new UnorderedAccessView(device, _packGpuBuf, new UnorderedAccessViewDescription
        {
            Format = Format.Unknown,
            Dimension = UnorderedAccessViewDimension.Buffer,
            Buffer = new UnorderedAccessViewDescription.BufferResource
            {
                FirstElement = 0,
                ElementCount = bytes / 4,
                Flags = UnorderedAccessViewBufferFlags.None,
            },
        });
        _packStagingBuf = new SharpDX.Direct3D11.Buffer(device, bytes, ResourceUsage.Staging, BindFlags.None, CpuAccessFlags.Read, ResourceOptionFlags.BufferStructured, 4);
    }

    // CPU 转换回退路径：BGRA 整帧 staging 纹理（尺寸变化重建）
    private void EnsureStagingTextureLocked(SharpDX.Direct3D11.Device device, int width, int height)
    {
        if (_stagingTexture == null || _stagingTexture.Description.Width != width ||
            _stagingTexture.Description.Height != height)
        {
            _stagingTexture?.Dispose();
            _stagingTexture = Direct3D11Helper.CreateStagingTexture(device, width, height, null);
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
        try
        {
            // 到达时机 = DWM 写完该帧、所有权刚释放——此时拷贝无跨进程同步等待
            lock (_lock)
            {
                if (!IsCapturing || _captureFramePool == null || _d3dDevice == null) return;

                using var frame = sender.TryGetNextFrame();
                if (frame == null) return;

                var frameTs = frame.SystemRelativeTime.Ticks;

                // ContentSize 变化 → Recreate + 刷新客户区（FFmpeg gfxcapture 同款）
                var contentSize = frame.ContentSize;
                if (contentSize.Width != _capSize.Width || contentSize.Height != _capSize.Height)
                {
                    _diagRecreates++;
                    Debug.WriteLine($"[WGC V2] Recreate: {_capSize.Width}x{_capSize.Height} -> {contentSize.Width}x{contentSize.Height}");
                    _captureFramePool.Recreate(_d3dDevice, _pixelFormat, 2, contentSize);
                    _capSize = contentSize;
                    (_region, _captureRect) = GetGameScreenInfo(_hWnd);

                    // 先挡住新的 Capture()，再等在途捕获退出（其可能正在锁外 Map staging/读缓存），
                    // 之后才允许销毁资源，否则会踩到已释放纹理
                    while (_activeCaptures > 0)
                    {
                        Monitor.Wait(_lock);
                    }

                    _packUav?.Dispose();
                    _packUav = null;
                    _packGpuBuf?.Dispose();
                    _packGpuBuf = null;
                    _packStagingBuf?.Dispose();
                    _packStagingBuf = null;
                    _stagingTexture?.Dispose();
                    _stagingTexture = null;
                    _gpuTexture?.Dispose();
                    _gpuTexture = null;
                    _cachedBgr?.Dispose();
                    _cachedBgr = null;
                    _cachedFrameTs = 0;
                    _gpuFrameTs = 0;
                    _hdrOutputTexture?.Dispose();
                    _hdrOutputTexture = null;
                    _hdrOutputUav?.Dispose();
                    _hdrOutputUav = null;
                    return;
                }

                using var surface = Direct3D11Helper.CreateSharpDXTexture2D(frame.Surface);
                var source = _isHdrEnabled ? ProcessHdrTexture(surface) : surface;
                var d3dDevice = source.Device;
                EnsureGpuTexture(d3dDevice, contentSize.Width, contentSize.Height, _region);
                EnsureDrawResources(d3dDevice);
                var context = d3dDevice.ImmediateContext;

                if (_useDrawWrite)
                {
                // FFmpeg gfxcapture 式 draw 写入：客户区裁剪折进 UV 窗口，一次 draw 渲染进自有纹理
                var drawStage = "EnsureDrawResources";
                try
                {
                    EnsureDrawResources(d3dDevice);

                    var srcW = source.Description.Width;
                    var srcH = source.Description.Height;
                    var region = _region;
                    var uvMinX = region == null ? 0f : region.Value.Left / (float)srcW;
                    var uvMinY = region == null ? 0f : region.Value.Top / (float)srcH;
                    var uvMaxX = region == null ? 1f : region.Value.Right / (float)srcW;
                    var uvMaxY = region == null ? 1f : region.Value.Bottom / (float)srcH;

                    drawStage = "MapConstantBuffer";
                    // 必须 WriteDiscard：cb 仍绑定在上一帧 draw 的 PS 槽上，普通 Write 会 E_INVALIDARG
                    var cbMap = context.MapSubresource(_drawCb, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);
                    Marshal.WriteInt32(cbMap.DataPointer, 0, BitConverter.SingleToInt32Bits(uvMinX));
                    Marshal.WriteInt32(cbMap.DataPointer, 4, BitConverter.SingleToInt32Bits(uvMinY));
                    Marshal.WriteInt32(cbMap.DataPointer, 8, BitConverter.SingleToInt32Bits(uvMaxX));
                    Marshal.WriteInt32(cbMap.DataPointer, 12, BitConverter.SingleToInt32Bits(uvMaxY));
                    context.UnmapSubresource(_drawCb, 0);

                    drawStage = "CreateSRV";
                    using var srv = new ShaderResourceView(d3dDevice, source);

                    drawStage = "Bind";
                    context.InputAssembler.PrimitiveTopology = SharpDX.Direct3D.PrimitiveTopology.TriangleList;
                    context.VertexShader.Set(_drawVs);
                    // VS 里也读 uvWindow（cb 寄存器），必须同样绑定，否则 uv 恒为 (0,0)、整屏变成单色
                    context.VertexShader.SetConstantBuffer(0, _drawCb);
                    context.PixelShader.Set(_drawPs);
                    context.PixelShader.SetSampler(0, _drawSampler);
                    context.PixelShader.SetConstantBuffer(0, _drawCb);
                    context.PixelShader.SetShaderResource(0, srv);
                    var vpW = region == null ? srcW : region.Value.Right - region.Value.Left;
                    var vpH = region == null ? srcH : region.Value.Bottom - region.Value.Top;
                    context.Rasterizer.SetViewport(0f, 0f, vpW, vpH);
                    context.OutputMerger.SetTargets(_gpuTextureRtv);

                    drawStage = "Draw";
                    context.Draw(3, 0);
                    context.PixelShader.SetShaderResource(0, null);
                    context.OutputMerger.SetTargets((RenderTargetView)null!);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"[WGC V2][draw] 失败于 {drawStage}: {e.Message}");
                    throw;
                }
                }
                else
                {
                    // 旧版拷贝路径（A/B 对比用）
                    if (_region != null)
                    {
                        context.CopySubresourceRegion(source, 0, _region, _gpuTexture, 0);
                    }
                    else
                    {
                        context.CopyResource(source, _gpuTexture);
                    }
                }

                _gpuFrameTs = frameTs;
                _diagFrames++;
            }
        }
        catch (SharpDXException e)
        {
            HandleSharpDxError(e);
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[WGC V2] FrameArrived 异常: {e.Message}");
        }
    }

    public GameCaptureFrame? Capture()
    {
        // 单飞闸门：并发消费方逐个进入
        lock (_captureGate)
        {
            // 阶段1（持 _lock）：状态校验、无新到达判定、GPU 打包 BGR24 + 拷入 staging buffer（同 tick）。
            // 临界区仅命令提交
            SharpDX.Direct3D11.Device? d3dDevice = null;
            RECT? rect;
            long frameTs;
            int gpuW = 0;
            int gpuH = 0;
            var isCacheHit = false;
            lock (_lock)
            {
                if (!IsCapturing || _gpuTexture == null || _gpuFrameTs == 0) return null;

                rect = _captureRect;
                frameTs = _gpuFrameTs;

                // 无新到达（广播纹理时间戳未变）：克隆缓存——零 GPU/PCIe/CvtColor 路径
                isCacheHit = _cachedBgr != null && _cachedFrameTs == frameTs;
                if (!isCacheHit)
                {
                    d3dDevice = _gpuTexture.Device;
                    gpuW = _gpuTexture.Description.Width;
                    gpuH = _gpuTexture.Description.Height;

                    var context = d3dDevice.ImmediateContext;

                    if (_useCpuConvert)
                    {
                        // CPU 转换回退路径：BGRA 整帧拷入 staging 纹理（阶段2 Map + CvtColor）
                        EnsureStagingTextureLocked(d3dDevice, gpuW, gpuH);
                        var ts1 = Stopwatch.GetTimestamp();
                        context.CopyResource(_gpuTexture, _stagingTexture);
                        _sumStageMs += (Stopwatch.GetTimestamp() - ts1) * 1000.0 / Stopwatch.Frequency;
                    }
                    else
                    {
                        EnsurePackResourcesLocked(d3dDevice, gpuW, gpuH);
                        var total = gpuW * gpuH;

                        // dims 常量（WriteDiscard：cb 可能仍绑定在上一帧 compute 槽上）
                        var ts1 = Stopwatch.GetTimestamp();
                        var cbMap = context.MapSubresource(_packCb, 0, MapMode.WriteDiscard, SharpDX.Direct3D11.MapFlags.None);
                        Marshal.WriteInt32(cbMap.DataPointer, 0, gpuW);
                        Marshal.WriteInt32(cbMap.DataPointer, 4, gpuH);
                        Marshal.WriteInt32(cbMap.DataPointer, 8, total);
                        context.UnmapSubresource(_packCb, 0);

                        // GPU 打包 BGR24 → staging buffer（异步入队，阶段2 Map 等待完成）
                        var ts2 = Stopwatch.GetTimestamp();
                        using var srv = new ShaderResourceView(d3dDevice, _gpuTexture);
                        var pixelGroups = (total + 3) / 4;   // 向上取整，与 EnsurePackResourcesLocked 的缓冲区分配一致
                        var groups = (pixelGroups + 63) / 64;
                        context.ComputeShader.Set(_packCs);
                        context.ComputeShader.SetShaderResource(0, srv);
                        context.ComputeShader.SetUnorderedAccessView(0, _packUav);
                        context.ComputeShader.SetConstantBuffer(0, _packCb);
                        context.Dispatch(groups, 1, 1);
                        context.ComputeShader.SetShaderResource(0, null);
                        context.ComputeShader.SetUnorderedAccessView(0, null);
                        context.ComputeShader.Set(null);
                        _sumPackMs += (Stopwatch.GetTimestamp() - ts2) * 1000.0 / Stopwatch.Frequency;

                        // 拷入 CPU 可读的 staging buffer
                        var ts3 = Stopwatch.GetTimestamp();
                        context.CopyResource(_packGpuBuf, _packStagingBuf);
                        _sumCopyMs += (Stopwatch.GetTimestamp() - ts3) * 1000.0 / Stopwatch.Frequency;
                    }
                }

                // 两条路径都计为在途：Stop/尺寸变化销毁 staging/_cachedBgr 前必须等其归零
                _activeCaptures++;

                _diagTicks++;
                if (isCacheHit) _diagCacheHits++;
                if (_diagTicks % 100 == 0)
                {
                    Debug.WriteLine(
                        $"[WGC V2][diag] ticks={_diagTicks} arrivals={_diagFrames} cacheHits={_diagCacheHits} recreates={_diagRecreates} " +
                        $"avgMs: pack={_sumPackMs / 100:0.00} copy={_sumCopyMs / 100:0.00} stage={_sumStageMs / 100:0.00} cvt={_sumCvtMs / 100:0.00} map={_sumMapMs / 100:0.00} clone={_sumCloneMs / 100:0.00}");
                    _sumPackMs = _sumCopyMs = _sumStageMs = _sumCvtMs = _sumMapMs = _sumCloneMs = 0;
                }
            }

            Mat? target = null;
            try
            {
                if (isCacheHit)
                {
                    // 无新帧：克隆缓存给消费者私有副本（零 GPU 拷贝、零 PCIe 回读、零 CvtColor）
                    var ts3 = Stopwatch.GetTimestamp();
                    target = AcquireBgrMat(_cachedBgr!.Rows, _cachedBgr.Cols);
                    _cachedBgr.CopyTo(target);
                    _sumCloneMs += (Stopwatch.GetTimestamp() - ts3) * 1000.0 / Stopwatch.Frequency;
                }
                else if (_useCpuConvert)
                {
                    // 阶段2（锁外，CPU 转换回退路径）：Map BGRA staging 纹理 → CvtColor 写缓存 → 克隆给消费者
                    var context = d3dDevice!.ImmediateContext;
                    var stagingDesc = _stagingTexture!.Description;
                    var ts2 = Stopwatch.GetTimestamp();
                    var dataBox = context.MapSubresource(_stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                    _sumMapMs += (Stopwatch.GetTimestamp() - ts2) * 1000.0 / Stopwatch.Frequency;
                    try
                    {
                        var ts3 = Stopwatch.GetTimestamp();
                        using var bgra = Mat.FromPixelData(stagingDesc.Height, stagingDesc.Width, MatType.CV_8UC4, dataBox.DataPointer, dataBox.RowPitch);
                        EnsureCache(stagingDesc.Width, stagingDesc.Height);
                        Cv2.CvtColor(bgra, _cachedBgr!, ColorConversionCodes.BGRA2BGR);
                        _cachedFrameTs = frameTs;
                        _sumCvtMs += (Stopwatch.GetTimestamp() - ts3) * 1000.0 / Stopwatch.Frequency;

                        var ts4 = Stopwatch.GetTimestamp();
                        target = AcquireBgrMat(stagingDesc.Height, stagingDesc.Width);
                        _cachedBgr!.CopyTo(target);
                        _sumCloneMs += (Stopwatch.GetTimestamp() - ts4) * 1000.0 / Stopwatch.Frequency;
                    }
                    finally
                    {
                        context.UnmapSubresource(_stagingTexture, 0);
                    }
                }
                else
                {
                    // 阶段2（锁外）：Map staging buffer——内容已是 GPU 打包好的 BGR24（= CV_8UC3 内存布局），
                    // 直接包装读取并写入缓存 + 克隆给消费者（CPU 零转换）
                    var context = d3dDevice!.ImmediateContext;
                    var ts2 = Stopwatch.GetTimestamp();
                    var dataBox = context.MapSubresource(_packStagingBuf, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                    _sumMapMs += (Stopwatch.GetTimestamp() - ts2) * 1000.0 / Stopwatch.Frequency;
                    try
                    {
                        using var bgr = Mat.FromPixelData(gpuH, gpuW, MatType.CV_8UC3, dataBox.DataPointer, gpuW * 3);
                        EnsureCache(gpuW, gpuH);
                        bgr.CopyTo(_cachedBgr!);
                        _cachedFrameTs = frameTs;

                        var ts4 = Stopwatch.GetTimestamp();
                        target = AcquireBgrMat(gpuH, gpuW);
                        _cachedBgr!.CopyTo(target);
                        _sumCloneMs += (Stopwatch.GetTimestamp() - ts4) * 1000.0 / Stopwatch.Frequency;
                    }
                    finally
                    {
                        context.UnmapSubresource(_packStagingBuf, 0);
                    }
                }

                return new GameCaptureFrame(WgcBgrMat.CreateFrom(target, ReleaseBgrMat), rect);
            }
            catch (SharpDXException e)
            {
                if (target != null)
                {
                    ReleaseBgrMat(target);
                }

                HandleSharpDxError(e);
                return null;
            }
            catch (Exception e)
            {
                if (target != null)
                {
                    ReleaseBgrMat(target);
                }

                // CvtColor/CopyTo 等 OpenCvSharp 异常：池化获取的 Mat 已归还（无泄漏）；
                // 缓存时间戳作废，避免下一 tick 误用残缺缓存
                lock (_lock)
                {
                    _cachedFrameTs = 0;
                }

                Debug.WriteLine($"[WGC V2] Capture 异常: {e.Message}");
                return null;
            }
            finally
            {
                lock (_lock)
                {
                    _activeCaptures--;
                    Monitor.PulseAll(_lock);
                }
            }
        }
    }

    private void EnsureCache(int width, int height)
    {
        if (_cachedBgr == null || _cachedBgr.Cols != width || _cachedBgr.Rows != height)
        {
            _cachedBgr?.Dispose();
            _cachedBgr = new Mat(height, width, MatType.CV_8UC3);
        }
    }

    private Mat AcquireBgrMat(int height, int width)
    {
        lock (_lock)
        {
            while (_bgrQueue.TryDequeue(out var mat))
            {
                if (_bgrBorrowed.Add(mat))
                {
                    if (mat.IsDisposed)
                    {
                        _bgrBorrowed.Remove(mat);
                        continue;
                    }
                    if (mat.Rows == height && mat.Cols == width && mat.Type() == MatType.CV_8UC3)
                    {
                        return mat;
                    }
                    _bgrBorrowed.Remove(mat);
                    mat.Dispose();
                }
                else
                {
                    // 防御分支：队内 Mat 不应同时存在于借出集合（账本错乱才可达）
                    var fresh = new Mat(height, width, MatType.CV_8UC3);
                    _bgrBorrowed.Add(fresh);
                    return fresh;
                }
            }
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
            if (mat == null || !_bgrBorrowed.Remove(mat))
            {
                mat?.Dispose();
                return;
            }
            if (mat.IsDisposed)
            {
                mat.Dispose();
                return;
            }
            if (_bgrPoolClosed)
            {
                mat.Dispose();
                return;
            }
            if (_bgrQueue.Count < MaxPoolSize)
            {
                _bgrQueue.Enqueue(mat);
            }
            else
            {
                if (_bgrQueue.TryDequeue(out var stale))
                {
                    stale.Dispose();
                }
                _bgrQueue.Enqueue(mat);
            }
        }
    }


    public void Stop()
    {
        lock (_lock)
        {
            IsCapturing = false;
            _hWnd = 0;
            if (_captureItem != null)
            {
                _captureItem.Closed -= CaptureItemOnClosed;
            }

            _captureSession?.Dispose();
            _captureSession = null;
            _captureFramePool?.Dispose();
            _captureFramePool = null;
            _captureItem = null;

            // IsCapturing=false 已挡住新的 Capture()；等待在途捕获（正锁外 Map/CvtColor）退出，
            // 才能销毁其正在使用的 staging/纹理/设备。Monitor.Wait 原子放锁，捕获 finally 归零时唤醒
            while (_activeCaptures > 0)
            {
                Monitor.Wait(_lock);
            }

            _gpuTextureRtv?.Dispose();
            _gpuTextureRtv = null;
            _gpuTexture?.Dispose();
            _gpuTexture = null;
            _drawVs?.Dispose();
            _drawVs = null;
            _drawPs?.Dispose();
            _drawPs = null;
            _drawSampler?.Dispose();
            _drawSampler = null;
            _drawCb?.Dispose();
            _drawCb = null;
            _packCs?.Dispose();
            _packCs = null;
            _packCb?.Dispose();
            _packCb = null;
            _packUav?.Dispose();
            _packUav = null;
            _packGpuBuf?.Dispose();
            _packGpuBuf = null;
            _packStagingBuf?.Dispose();
            _packStagingBuf = null;
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _cachedBgr?.Dispose();
            _cachedBgr = null;
            _cachedFrameTs = 0;
            while (_bgrQueue.TryDequeue(out var pooled)) pooled.Dispose();
            _bgrPoolClosed = true;
            _hdrOutputTexture?.Dispose();
            _hdrOutputTexture = null;
            _hdrOutputUav?.Dispose();
            _hdrOutputUav = null;
            _hdrComputeShader?.Dispose();
            _hdrComputeShader = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
        }
    }

    private void CaptureItemOnClosed(GraphicsCaptureItem sender, object args)
    {
        Stop();
    }

    /// <summary>
    /// FFmpeg gfxcapture 同款：把 WGC 推帧率限到消费需求率（26100+，IGraphicsCaptureSession5）。
    /// C# 投影（TFM 22621）不含该接口，走 QI + vtable 直调；不可用/失败仅告警降级，不影响捕获。
    /// </summary>
    private void TryApplyMinUpdateInterval(GraphicsCaptureSession session)
    {
        if (_minUpdateIntervalHns <= 0)
        {
            Debug.WriteLine("[WGC V2] MinUpdateInterval=0，不启用限流");
            return;
        }

        if (!ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "MinUpdateInterval"))
        {
            Debug.WriteLine("[WGC V2] MinUpdateInterval 不可用，跳过限流（取帧逻辑不依赖它）");
            return;
        }

        try
        {
            // IGraphicsCaptureSession5 {67C0EA62-1F85-5061-925A-239BE0AC09CB}
            var iid = GraphicsCaptureSession5Iid;
            var unknown = Marshal.GetIUnknownForObject(session);
            try
            {
                if (Marshal.QueryInterface(unknown, ref iid, out var session5) != 0)
                {
                    Debug.WriteLine("[WGC V2] QueryInterface IGraphicsCaptureSession5 失败，跳过限流");
                    return;
                }

                try
                {
                    // vtable 布局：IUnknown 0-2 + IInspectable 3-5 + get_MinUpdateInterval 6 + put_MinUpdateInterval 7
                    var vtbl = Marshal.ReadIntPtr(session5);
                    var putPtr = Marshal.ReadIntPtr(vtbl, PutMinUpdateIntervalVtableSlot * IntPtr.Size);
                    var put = Marshal.GetDelegateForFunctionPointer<PutMinUpdateIntervalDelegate>(putPtr);
                    var hr = put(session5, _minUpdateIntervalHns);
                    if (hr < 0)
                    {
                        Debug.WriteLine($"[WGC V2] put_MinUpdateInterval 失败: 0x{hr:X8}");
                    }
                    else
                    {
                        Debug.WriteLine($"[WGC V2] MinUpdateInterval 已设置: {_minUpdateIntervalHns / 10000.0:0.##}ms");
                    }
                }
                finally
                {
                    Marshal.Release(session5);
                }
            }
            finally
            {
                Marshal.Release(unknown);
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[WGC V2] 应用 MinUpdateInterval 异常: {e.Message}");
        }
    }
}
