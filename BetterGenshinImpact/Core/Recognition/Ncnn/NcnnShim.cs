using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>单次推理的输入；float CHW 与 8bit 像素二选一。</summary>
public sealed record NcnnInput
{
    public required string Name { get; init; }
    public float[]? FloatData { get; init; }
    public byte[]? PixelData { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Channels { get; init; } = 3;
    public NcnnPixelFormat PixelFormat { get; init; } = NcnnPixelFormat.FloatChw;
    public int Stride { get; init; }

    public static NcnnInput FromFloat(string name, float[] data, int width, int height, int channels) =>
        new() { Name = name, FloatData = data, Width = width, Height = height, Channels = channels };

    public static NcnnInput FromPixels(string name, byte[] pixels, int width, int height,
        NcnnPixelFormat format, int stride = 0) =>
        new()
        {
            Name = name, PixelData = pixels, Width = width, Height = height,
            Channels = 3, PixelFormat = format, Stride = stride
        };
}

/// <summary>一次推理的输出：元信息 + 紧凑 fp32 数据。</summary>
public sealed record NcnnOutput(NcnnTensorInfo Info, float[] Data);

/// <summary>
///     ncnn_shim.dll 的动态加载封装（NativeLibrary 裸名解析：普通构建探测 deps.json 的
///     runtimes/win-x64/native/，单文件发布探测 exe 旁，两种布局由 .NET 标准机制自动处理）。
///     旧接口始终可用；新接口（版本化选项、富元信息、指针直输、设备详情）按导出探测，缺失时降级。
///     会话级互操作经由 <see cref="INcnnShim" /> 抽象，便于推理层单测注入托管替身。
/// </summary>
public sealed class NcnnShim : INcnnShim
{
    private readonly nint _library;
    private readonly DestroyDelegate _destroy;
    private readonly FreeBufferDelegate _freeBuffer;
    private readonly CreateExDelegate _createEx;
    private readonly InferExDelegate _inferEx;
    private readonly GetCountDelegate _getInputCount;
    private readonly GetCountDelegate _getOutputCount;
    private readonly GetNameDelegate _getInputName;
    private readonly GetNameDelegate _getOutputName;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyDelegate(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeBufferDelegate(nint buffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateExDelegate(nint options);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InferExDelegate(
        nint handle, nint inputs, int inputCount,
        nint outputNames, int outputCount,
        nint extractorOptions, nint outputs);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetCountDelegate(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetNameDelegate(nint handle, int index, [Out] byte[] buffer, int bufferSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DefaultOptionsDelegate(nint options);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceInfoDelegate(int index, nint info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint HeapBudgetDelegate(int index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceCountDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DeviceNameDelegate(int index, [Out] byte[] buffer, int bufferSize);

    /// <summary>
    ///     加载 shim 并绑定全部必需导出；缺少任一导出直接抛错，不再支持旧版 DLL。
    ///     <paramref name="dllPath" /> 为空时按标准机制裸名解析（测试可用显式路径注入替身）。
    /// </summary>
    public NcnnShim(string? dllPath = null)
    {
        _library = GetLibrary(dllPath);
        _createEx = GetExport<CreateExDelegate>(_library, "shim_create_ex");
        _inferEx = GetExport<InferExDelegate>(_library, "shim_infer_ex");
        _destroy = GetExport<DestroyDelegate>(_library, "shim_destroy");
        _freeBuffer = GetExport<FreeBufferDelegate>(_library, "shim_free_buffer");
        _getInputCount = GetExport<GetCountDelegate>(_library, "shim_get_input_count");
        _getOutputCount = GetExport<GetCountDelegate>(_library, "shim_get_output_count");
        _getInputName = GetExport<GetNameDelegate>(_library, "shim_get_input_name");
        _getOutputName = GetExport<GetNameDelegate>(_library, "shim_get_output_name");
    }

    /// <summary>只探测导出表、不构造会话：供启动校验与设置页判断运行库版本，永远不抛异常。</summary>
    public static bool IsSupported()
    {
        try
        {
            var library = GetLibrary(null);
            return NativeLibrary.TryGetExport(library, "shim_create_ex", out _)
                && NativeLibrary.TryGetExport(library, "shim_infer_ex", out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>创建会话：全部参数经由版本化结构下发，且在 load_param 之前写入。</summary>
    public nint Create(NcnnNetOptions options)
    {
        // 原生侧在 shim_create_ex 内部同步读取路径字符串，必须等调用返回后才能释放。
        using var native = NativeOptions.Create(options);
        return _createEx(native.Pointer);
    }

    public void Destroy(nint handle) => _destroy(handle);

    /// <summary>
    ///     多输入多输出推理：输出带 dims/w/h/c/elembits/cstep，数据已由 shim 拷成紧凑 fp32。
    /// </summary>
    public IReadOnlyList<NcnnOutput> InferEx(nint handle,
        IReadOnlyList<NcnnInput> inputs, IReadOnlyList<string> outputNames,
        NcnnExtractorOptions? extractor = null)
    {
        if (inputs.Count == 0 || outputNames.Count == 0) return [];

        var pinned = new List<GCHandle>(inputs.Count);
        var nameHandles = new List<nint>(inputs.Count + outputNames.Count);
        nint inputsPtr = nint.Zero;
        nint namesPtr = nint.Zero;
        nint outputsPtr = nint.Zero;
        nint extractorPtr = nint.Zero;

        try
        {
            var inputSize = Marshal.SizeOf<ShimInput>();
            inputsPtr = Marshal.AllocHGlobal(inputSize * inputs.Count);
            for (var i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i];
                nint dataPtr;
                if (input.PixelData != null)
                {
                    var pin = GCHandle.Alloc(input.PixelData, GCHandleType.Pinned);
                    pinned.Add(pin);
                    dataPtr = pin.AddrOfPinnedObject();
                }
                else if (input.FloatData != null)
                {
                    var pin = GCHandle.Alloc(input.FloatData, GCHandleType.Pinned);
                    pinned.Add(pin);
                    dataPtr = pin.AddrOfPinnedObject();
                }
                else
                {
                    throw new ArgumentException($"输入 {input.Name} 没有数据。");
                }

                var namePtr = Marshal.StringToHGlobalAnsi(input.Name);
                nameHandles.Add(namePtr);
                var native = new ShimInput
                {
                    Name = namePtr,
                    Data = dataPtr,
                    Width = input.Width,
                    Height = input.Height,
                    Channels = input.Channels,
                    PixelFormat = (int)input.PixelFormat,
                    Stride = input.Stride
                };
                Marshal.StructureToPtr(native, inputsPtr + i * inputSize, false);
            }

            namesPtr = Marshal.AllocHGlobal(IntPtr.Size * outputNames.Count);
            for (var i = 0; i < outputNames.Count; i++)
            {
                var namePtr = Marshal.StringToHGlobalAnsi(outputNames[i]);
                nameHandles.Add(namePtr);
                Marshal.WriteIntPtr(namesPtr, i * IntPtr.Size, namePtr);
            }

            if (extractor != null)
            {
                extractorPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ShimExtractorOptions>());
                Marshal.StructureToPtr(new ShimExtractorOptions
                {
                    Size = Marshal.SizeOf<ShimExtractorOptions>(),
                    NumThreads = extractor.NumThreads,
                    LightMode = extractor.LightMode
                }, extractorPtr, false);
            }

            var tensorSize = Marshal.SizeOf<ShimTensor>();
            outputsPtr = Marshal.AllocHGlobal(tensorSize * outputNames.Count);
            var rc = _inferEx(handle, inputsPtr, inputs.Count, namesPtr, outputNames.Count, extractorPtr, outputsPtr);
            if (rc != 0) throw new InvalidOperationException($"ncnn infer_ex 失败，错误码 {rc}。");

            var result = new List<NcnnOutput>(outputNames.Count);
            for (var i = 0; i < outputNames.Count; i++)
            {
                var tensor = Marshal.PtrToStructure<ShimTensor>(outputsPtr + i * tensorSize);
                var info = new NcnnTensorInfo(tensor.Dims, tensor.Width, tensor.Height, tensor.Depth,
                    tensor.Channels, tensor.ElementPack, tensor.ElementBits, tensor.ChannelStep, tensor.Count);
                var data = new float[tensor.Count];
                if (tensor.Count > 0 && tensor.Data != nint.Zero)
                {
                    Marshal.Copy(tensor.Data, data, 0, tensor.Count);
                    _freeBuffer(tensor.Data);
                }

                result.Add(new NcnnOutput(info, data));
            }

            return result;
        }
        finally
        {
            foreach (var pin in pinned) pin.Free();
            foreach (var ptr in nameHandles) Marshal.FreeHGlobal(ptr);
            if (inputsPtr != nint.Zero) Marshal.FreeHGlobal(inputsPtr);
            if (namesPtr != nint.Zero) Marshal.FreeHGlobal(namesPtr);
            if (extractorPtr != nint.Zero) Marshal.FreeHGlobal(extractorPtr);
            if (outputsPtr != nint.Zero) Marshal.FreeHGlobal(outputsPtr);
        }
    }

    /// <summary>像素直输：RGB/BGR/RGBA/BGRA/NV12 直接喂入，省掉一次 CHW 浮点写入。</summary>
    public NcnnOutput? InferPixels(nint handle, byte[] pixels, int width, int height,
        NcnnPixelFormat format, string inputName, string outputName, int stride = 0)
    {
        var outputs = InferEx(handle,
            [NcnnInput.FromPixels(inputName, pixels, width, height, format, stride)],
            [outputName]);
        return outputs.Count > 0 ? outputs[0] : null;
    }

    public string[] QueryInputBlobs(nint handle) => QueryBlobNames(handle, _getInputCount, _getInputName);
    public string[] QueryOutputBlobs(nint handle) => QueryBlobNames(handle, _getOutputCount, _getOutputName);

    private static string[] QueryBlobNames(nint handle, GetCountDelegate countFn, GetNameDelegate nameFn)
    {
        var count = countFn(handle);
        if (count <= 0) return [];
        var names = new string[count];
        var buffer = new byte[256];
        for (var i = 0; i < count; i++)
        {
            if (nameFn(handle, i, buffer, buffer.Length) != 0) continue;
            var end = Array.IndexOf(buffer, (byte)0);
            names[i] = Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
        }

        return names;
    }

    // ---------------- 静态：设备与默认参数查询 ----------------

    private static readonly object LibraryLock = new();
    private static readonly Dictionary<string, nint> Libraries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object DeviceLock = new();
    private static NcnnDeviceQuery? _devices;

    private static nint GetLibrary(string? dllPath)
    {
        lock (LibraryLock)
        {
            if (Libraries.TryGetValue(dllPath, out var library)) return library;
            if (string.IsNullOrEmpty(dllPath))
            {
                // 裸名解析：普通构建命中 deps.json 的 runtimes/win-x64/native/，
                // 单文件发布命中 exe 旁——两种布局由 .NET 标准机制处理
                if (!NativeLibrary.TryLoad("ncnn_shim", typeof(NcnnShim).Assembly, null, out library))
                    throw new NcnnUnavailableException(
                        "ncnn_shim.dll 加载失败：未找到运行库（应由 NuGet 包 BetterGI.NcnnShim 随构建部署）。");
            }
            else
            {
                library = NativeLibrary.Load(dllPath);
            }
            Libraries[dllPath] = library;
            return library;
        }
    }

    /// <summary>
    ///     枚举 Vulkan 设备（首次调用会初始化 ncnn 的 Vulkan 实例，之后随进程常驻）。
    ///     运行库缺失或导出缺失直接抛错，由启动校验转成可读提示。
    /// </summary>
    public static NcnnDeviceQuery QueryDevices(string? dllPath = null)
    {
        lock (DeviceLock)
        {
            return _devices ??= QueryDevicesCore(dllPath);
        }
    }

    public static void ResetDeviceCache()
    {
        lock (DeviceLock)
        {
            _devices = null;
        }
    }

    private static nint RequireLibrary(string? dllPath)
    {
        // 裸名模式下由 GetLibrary 在找不到时抛 NcnnUnavailableException；显式路径仍保留存在性检查
        if (!string.IsNullOrEmpty(dllPath) && !File.Exists(dllPath))
            throw new NcnnUnavailableException($"ncnn 运行库缺失：{dllPath}");
        return GetLibrary(dllPath);
    }

    private static NcnnDeviceQuery QueryDevicesCore(string dllPath)
    {
        var library = RequireLibrary(dllPath);
        if (!NativeLibrary.TryGetExport(library, "shim_get_device_count", out var countAddress)
            || !NativeLibrary.TryGetExport(library, "shim_get_device_name", out var nameAddress))
            throw new NotSupportedException(
                "ncnn_shim.dll 版本过旧：缺少 shim_get_device_count / shim_get_device_name，无法枚举 Vulkan 设备。");

        var getCount = Marshal.GetDelegateForFunctionPointer<DeviceCountDelegate>(countAddress);
        var getName = Marshal.GetDelegateForFunctionPointer<DeviceNameDelegate>(nameAddress);
        var defaultIndex = NativeLibrary.TryGetExport(library, "shim_get_default_device", out var defaultAddress)
            ? Marshal.GetDelegateForFunctionPointer<DeviceCountDelegate>(defaultAddress)()
            : -1;

        var count = getCount();
        var devices = new List<NcnnDeviceInfo>(count);
        var buffer = new byte[256];
        for (var i = 0; i < count; i++)
        {
            var typeCode = getName(i, buffer, buffer.Length);
            if (typeCode < 0) continue;
            var end = Array.IndexOf(buffer, (byte)0);
            var name = Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
            devices.Add(new NcnnDeviceInfo(i,
                string.IsNullOrWhiteSpace(name) ? $"Vulkan 设备 {i}" : name, typeCode));
        }

        return new NcnnDeviceQuery(devices, defaultIndex);
    }

    /// <summary>读取单个设备的完整能力与显存预算；查询失败即抛错，不返回半残详情。</summary>
    public static NcnnDeviceDetails QueryDeviceDetails(int index, string? dllPath = null)
    {
        var library = RequireLibrary(dllPath);
        if (!NativeLibrary.TryGetExport(library, "shim_get_device_info", out var address))
            throw new NotSupportedException("ncnn_shim.dll 版本过旧：缺少 shim_get_device_info，无法读取设备详情。");

        var getInfo = Marshal.GetDelegateForFunctionPointer<DeviceInfoDelegate>(address);
        var size = Marshal.SizeOf<ShimDeviceInfo>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new ShimDeviceInfo { Size = size }, ptr, false);
            if (getInfo(index, ptr) != 0)
                throw new NcnnUnavailableException($"读取 Vulkan 设备 {index} 的详情失败，请检查显卡驱动或改用 CPU 计算。");
            var info = Marshal.PtrToStructure<ShimDeviceInfo>(ptr);
            return new NcnnDeviceDetails(index, info.Name ?? "", info.DriverName ?? "", info.Type,
                info.VendorId, info.DeviceId, info.ApiVersion, info.DriverVersion, info.DriverId,
                info.ComputeQueueCount, info.TransferQueueCount,
                info.UnifiedComputeTransferQueue != 0, info.ResizableBarEnabled != 0,
                info.SupportImageStorage != 0, info.PreferShaderLocalMemory != 0,
                info.SupportFp16Packed != 0, info.SupportFp16Storage != 0,
                info.SupportFp16Uniform != 0, info.SupportFp16Arithmetic != 0,
                info.SupportInt8Packed != 0, info.SupportInt8Storage != 0,
                info.SupportInt8Uniform != 0, info.SupportInt8Arithmetic != 0,
                info.SupportInt16Packed != 0, info.SupportInt16Storage != 0, info.SupportInt16Arithmetic != 0,
                info.SupportBf16Packed != 0, info.SupportBf16Storage != 0,
                info.SupportFp16Image != 0, info.SupportInt8Image != 0, info.SupportFpFfastMath != 0,
                info.SupportSubgroupSizeControl != 0, info.SupportComputeFullSubgroups != 0,
                info.SubgroupSize, info.MinSubgroupSize, info.MaxSubgroupSize,
                info.MaxSharedMemorySize, info.MaxWorkgroupInvocations, info.RoughScore, info.HeapBudgetMB);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>显存预算（MB）；驱动未提供预算时返回 0 表示未知。</summary>
    public static uint QueryHeapBudget(int index, string? dllPath = null)
    {
        var library = RequireLibrary(dllPath);
        if (!NativeLibrary.TryGetExport(library, "shim_get_heap_budget", out var address))
            throw new NotSupportedException("ncnn_shim.dll 版本过旧：缺少 shim_get_heap_budget。");
        return Marshal.GetDelegateForFunctionPointer<HeapBudgetDelegate>(address)(index);
    }

    /// <summary>
    ///     读取 ncnn::Option 的真实默认值；缺导出抛 <see cref="NotSupportedException" />，
    ///     由调用方决定是否跳过默认值对齐（对齐失败不影响推理正确性）。
    /// </summary>
    public static NcnnNetOptions QueryDefaultOptions(string? dllPath = null)
    {
        var library = RequireLibrary(dllPath);
        if (!NativeLibrary.TryGetExport(library, "shim_get_default_options", out var address))
            throw new NotSupportedException("ncnn_shim.dll 版本过旧：缺少 shim_get_default_options。");

        var getDefaults = Marshal.GetDelegateForFunctionPointer<DefaultOptionsDelegate>(address);
        var size = Marshal.SizeOf<ShimNetOptions>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(new ShimNetOptions { Size = size }, ptr, false);
            getDefaults(ptr);
            var o = Marshal.PtrToStructure<ShimNetOptions>(ptr);
            return new NcnnNetOptions
            {
                Threads = o.NumThreads > 0 ? o.NumThreads : 4,
                OpenmpBlocktime = o.OpenmpBlocktime,
                UseWinogradConvolution = o.UseWinogradConvolution != 0,
                UseSgemmConvolution = o.UseSgemmConvolution != 0,
                UseInt8Inference = o.UseInt8Inference != 0,
                UseBf16Storage = o.UseBf16Storage != 0,
                UseFp16Packed = o.UseFp16Packed != 0,
                UseFp16Storage = o.UseFp16Storage != 0,
                UseFp16Arithmetic = o.UseFp16Arithmetic != 0,
                UseInt8Packed = o.UseInt8Packed != 0,
                UseInt8Storage = o.UseInt8Storage != 0,
                UseInt8Arithmetic = o.UseInt8Arithmetic != 0,
                UsePackingLayout = o.UsePackingLayout != 0,
                UseBf16Packed = o.UseBf16Packed != 0,
                UseShaderLocalMemory = o.UseShaderLocalMemory != 0,
                UseCooperativeMatrix = o.UseCooperativeMatrix != 0,
                UseWinograd23 = o.UseWinograd23 != 0,
                UseWinograd43 = o.UseWinograd43 != 0,
                UseWinograd63 = o.UseWinograd63 != 0,
                UseA53A55OptimizedKernel = o.UseA53A55OptimizedKernel != 0,
                UseFp16Uniform = o.UseFp16Uniform != 0,
                UseInt8Uniform = o.UseInt8Uniform != 0,
                UseInt16Packed = o.UseInt16Packed != 0,
                UseInt16Storage = o.UseInt16Storage != 0,
                UseLocalPoolAllocator = o.UseLocalPoolAllocator != 0,
                FlushDenormals = o.FlushDenormals
            };
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    ///     版本化选项的原生块与字符串生命周期容器：<see cref="Pointer" /> 只在 <c>shim_create_ex</c>
    ///     调用期间有效，调用返回后即可整体释放（ncnn 在 load_param/load_model 中同步读完路径）。
    /// </summary>
    private sealed class NativeOptions : IDisposable
    {
        private readonly List<nint> _strings = new(3);

        public nint Pointer { get; private set; }

        public static NativeOptions Create(NcnnNetOptions options)
        {
            var holder = new NativeOptions { Pointer = Marshal.AllocHGlobal(Marshal.SizeOf<ShimNetOptions>()) };
            try
            {
                var paramPath = holder.AllocString(options.ParamPath);
                var modelPath = holder.AllocString(options.ModelPath);
                var paramMemory = options.ParamMemory == null ? nint.Zero : holder.AllocString(options.ParamMemory);
                var native = new ShimNetOptions
                {
                    Size = Marshal.SizeOf<ShimNetOptions>(),
                    ParamPath = paramPath,
                    ModelPath = modelPath,
                    ParamMemory = paramMemory,
                    GpuIndex = options.GpuIndex,
                    NumThreads = options.Threads,
                    OpenmpBlocktime = options.OpenmpBlocktime,
                    UseWinogradConvolution = options.UseWinogradConvolution ? 1 : 0,
                    UseSgemmConvolution = options.UseSgemmConvolution ? 1 : 0,
                    UseInt8Inference = options.UseInt8Inference ? 1 : 0,
                    UseBf16Storage = options.UseBf16Storage ? 1 : 0,
                    UseFp16Packed = options.UseFp16Packed ? 1 : 0,
                    UseFp16Storage = options.UseFp16Storage ? 1 : 0,
                    UseFp16Arithmetic = options.UseFp16Arithmetic ? 1 : 0,
                    UseInt8Packed = options.UseInt8Packed ? 1 : 0,
                    UseInt8Storage = options.UseInt8Storage ? 1 : 0,
                    UseInt8Arithmetic = options.UseInt8Arithmetic ? 1 : 0,
                    UsePackingLayout = options.UsePackingLayout ? 1 : 0,
                    UseBf16Packed = options.UseBf16Packed ? 1 : 0,
                    UseShaderLocalMemory = options.UseShaderLocalMemory ? 1 : 0,
                    UseCooperativeMatrix = options.UseCooperativeMatrix ? 1 : 0,
                    UseWinograd23 = options.UseWinograd23 ? 1 : 0,
                    UseWinograd43 = options.UseWinograd43 ? 1 : 0,
                    UseWinograd63 = options.UseWinograd63 ? 1 : 0,
                    UseA53A55OptimizedKernel = options.UseA53A55OptimizedKernel ? 1 : 0,
                    UseFp16Uniform = options.UseFp16Uniform ? 1 : 0,
                    UseInt8Uniform = options.UseInt8Uniform ? 1 : 0,
                    UseInt16Packed = options.UseInt16Packed ? 1 : 0,
                    UseInt16Storage = options.UseInt16Storage ? 1 : 0,
                    UseLocalPoolAllocator = options.UseLocalPoolAllocator ? 1 : 0,
                    FlushDenormals = options.FlushDenormals
                };
                Marshal.StructureToPtr(native, holder.Pointer, false);
                return holder;
            }
            catch
            {
                holder.Dispose();
                throw;
            }
        }

        private nint AllocString(string value)
        {
            var ptr = Marshal.StringToHGlobalAnsi(value);
            _strings.Add(ptr);
            return ptr;
        }

        public void Dispose()
        {
            if (Pointer != nint.Zero)
            {
                Marshal.FreeHGlobal(Pointer);
                Pointer = nint.Zero;
            }

            foreach (var ptr in _strings) Marshal.FreeHGlobal(ptr);
            _strings.Clear();
        }
    }

    private static T GetExport<T>(nint library, string name) where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(library, name, out var address))
            throw new EntryPointNotFoundException($"ncnn_shim 导出未找到: {name}");
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static T? TryGetDelegate<T>(nint library, string name) where T : Delegate
    {
        return NativeLibrary.TryGetExport(library, name, out var address)
            ? Marshal.GetDelegateForFunctionPointer<T>(address)
            : null;
    }

    public void Dispose()
    {
        // 故意不调用 NativeLibrary.Free：
        // 卸载 ncnn_shim 会执行其 CRT exit 表（execute_onexit_table），静态析构销毁 Vulkan 全局对象，
        // 实测在进程退出阶段直接 FAST_FAIL_FATAL_APP_EXIT（0xc0000409），把整个宿主一起带走。
        // 库句柄有意泄漏随进程存活，net 句柄已由各持有方 shim_destroy 释放。
    }
}

/// <summary>单次推理的 extractor 级选项。</summary>
public sealed record NcnnExtractorOptions
{
    public int NumThreads { get; init; }
    public int LightMode { get; init; } = -1;
}
