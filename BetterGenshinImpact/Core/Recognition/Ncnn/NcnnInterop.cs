using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.Core.Recognition.Ncnn;

/// <summary>shim 的输入像素格式；FloatChw 为传统的 float 平面输入。</summary>
public enum NcnnPixelFormat
{
    FloatChw = 0,
    Rgb888 = 1,
    Bgr888 = 2,
    Rgba8888 = 3,
    Bgra8888 = 4,
    Nv12 = 5
}

/// <summary>
///     与 <c>ShimNetOptions</c> 逐字段对应；<c>Size</c> 必须填 <see cref="Marshal.SizeOf{T}" /> 的结果。
///     bool 字段在原生侧是 int（0/1），-1 表示沿用 ncnn 默认。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ShimNetOptions
{
    public int Size;
    public IntPtr ParamPath;
    public IntPtr ModelPath;
    public IntPtr ParamMemory;
    public int GpuIndex;
    public int NumThreads;
    public int OpenmpBlocktime;
    public int UseWinogradConvolution;
    public int UseSgemmConvolution;
    public int UseInt8Inference;
    public int UseBf16Storage;
    public int UseFp16Packed;
    public int UseFp16Storage;
    public int UseFp16Arithmetic;
    public int UseInt8Packed;
    public int UseInt8Storage;
    public int UseInt8Arithmetic;
    public int UsePackingLayout;
    public int UseBf16Packed;
    public int UseShaderLocalMemory;
    public int UseCooperativeMatrix;
    public int UseWinograd23;
    public int UseWinograd43;
    public int UseWinograd63;
    public int UseA53A55OptimizedKernel;
    public int UseFp16Uniform;
    public int UseInt8Uniform;
    public int UseInt16Packed;
    public int UseInt16Storage;
    public int UseLocalPoolAllocator;
    public int FlushDenormals;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ShimInput
{
    public IntPtr Name;
    public IntPtr Data;
    public int Width;
    public int Height;
    public int Channels;
    public int PixelFormat;
    public int Stride;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ShimTensor
{
    public IntPtr Data;
    public int Dims;
    public int Width;
    public int Height;
    public int Depth;
    public int Channels;
    public int ElementPack;
    public int ElementBits;
    public int ChannelStep;
    public int Count;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ShimExtractorOptions
{
    public int Size;
    public int NumThreads;
    public int LightMode;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct ShimDeviceInfo
{
    public int Size;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string Name;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string DriverName;

    public int Type;
    public uint VendorId;
    public uint DeviceId;
    public uint ApiVersion;
    public uint DriverVersion;
    public uint DriverId;
    public uint ComputeQueueCount;
    public uint TransferQueueCount;
    public int UnifiedComputeTransferQueue;
    public int ResizableBarEnabled;
    public int SupportImageStorage;
    public int PreferShaderLocalMemory;
    public int SupportFp16Packed;
    public int SupportFp16Storage;
    public int SupportFp16Uniform;
    public int SupportFp16Arithmetic;
    public int SupportInt8Packed;
    public int SupportInt8Storage;
    public int SupportInt8Uniform;
    public int SupportInt8Arithmetic;
    public int SupportInt16Packed;
    public int SupportInt16Storage;
    public int SupportInt16Arithmetic;
    public int SupportBf16Packed;
    public int SupportBf16Storage;
    public int SupportFp16Image;
    public int SupportInt8Image;
    public int SupportFpFfastMath;
    public int SupportSubgroupSizeControl;
    public int SupportComputeFullSubgroups;
    public uint SupportSubgroupOps;
    public uint SubgroupSize;
    public uint MinSubgroupSize;
    public uint MaxSubgroupSize;
    public uint MaxSharedMemorySize;
    public uint MaxWorkgroupInvocations;
    public uint RoughScore;
    public uint HeapBudgetMB;
}

/// <summary>一次推理输出的元信息；<see cref="Count" /> 是紧凑 fp32 元素数（已去掉 cstep 对齐填充）。</summary>
public sealed record NcnnTensorInfo(
    int Dims, int Width, int Height, int Depth, int Channels,
    int ElementPack, int ElementBits, int ChannelStep, int Count)
{
    public int CompactCount => Width * Height * Math.Max(Depth, 1) * Channels;

    public string ShapeText => Dims switch
    {
        <= 1 => $"{Width}",
        2 => $"{Width}×{Height}",
        3 => $"{Width}×{Height}×{Channels}",
        _ => $"{Width}×{Height}×{Depth}×{Channels}"
    };
}

/// <summary>ncnn 会话的完整调优参数；默认值与 ncnn::Option 一致。</summary>
public sealed record NcnnNetOptions
{
    public string ParamPath { get; init; } = "";
    public string ModelPath { get; init; } = "";
    public string? ParamMemory { get; init; }
    public int GpuIndex { get; init; } = -1;
    public int Threads { get; init; } = 4;
    public int OpenmpBlocktime { get; init; } = 20;

    public bool UseWinogradConvolution { get; init; } = true;
    public bool UseSgemmConvolution { get; init; } = true;
    public bool UseInt8Inference { get; init; } = true;
    public bool UseBf16Storage { get; init; }
    public bool UseFp16Packed { get; init; } = true;
    public bool UseFp16Storage { get; init; } = true;
    public bool UseFp16Arithmetic { get; init; } = true;
    public bool UseInt8Packed { get; init; } = true;
    public bool UseInt8Storage { get; init; } = true;
    public bool UseInt8Arithmetic { get; init; } = true;
    public bool UsePackingLayout { get; init; } = true;

    /// <summary>
    ///     与 ncnn::Option 默认一致为 false。开启 bf16 packed 但设备不支持 bf16 cooperative matrix 时，
    ///     ncnn 20261020 会生成编译失败的 coopmat 着色器（B580 实测直接崩溃），因此不能默认打开。
    /// </summary>
    public bool UseBf16Packed { get; init; }
    public bool UseShaderLocalMemory { get; init; } = true;
    public bool UseCooperativeMatrix { get; init; } = true;
    public bool UseWinograd23 { get; init; } = true;
    public bool UseWinograd43 { get; init; } = true;
    public bool UseWinograd63 { get; init; } = true;
    public bool UseA53A55OptimizedKernel { get; init; }
    public bool UseFp16Uniform { get; init; } = true;
    public bool UseInt8Uniform { get; init; } = true;
    public bool UseInt16Packed { get; init; } = true;
    public bool UseInt16Storage { get; init; } = true;
    public bool UseLocalPoolAllocator { get; init; } = true;
    public int FlushDenormals { get; init; } = 3;
}

/// <summary>单个 Vulkan 设备的完整能力与运行时信息（对应 shim_get_device_info）。</summary>
public sealed record NcnnDeviceDetails(
    int Index, string Name, string DriverName, int TypeCode,
    uint VendorId, uint DeviceId, uint ApiVersion, uint DriverVersion, uint DriverId,
    uint ComputeQueueCount, uint TransferQueueCount,
    bool UnifiedComputeTransferQueue, bool ResizableBarEnabled,
    bool SupportImageStorage, bool PreferShaderLocalMemory,
    bool SupportFp16Packed, bool SupportFp16Storage, bool SupportFp16Uniform, bool SupportFp16Arithmetic,
    bool SupportInt8Packed, bool SupportInt8Storage, bool SupportInt8Uniform, bool SupportInt8Arithmetic,
    bool SupportInt16Packed, bool SupportInt16Storage, bool SupportInt16Arithmetic,
    bool SupportBf16Packed, bool SupportBf16Storage,
    bool SupportFp16Image, bool SupportInt8Image, bool SupportFpFastMath,
    bool SupportSubgroupSizeControl, bool SupportComputeFullSubgroups,
    uint SubgroupSize, uint MinSubgroupSize, uint MaxSubgroupSize,
    uint MaxSharedMemorySize, uint MaxWorkgroupInvocations, uint RoughScore, uint HeapBudgetMB)
{
    public string TypeName => TypeCode switch
    {
        0 => "独显",
        1 => "核显",
        2 => "虚拟显卡",
        3 => "CPU",
        _ => "未知类型"
    };

    public string DisplayName => $"#{Index} {Name}（{TypeName}）";

    public string ApiVersionText =>
        $"{ApiVersion >> 22}.{(ApiVersion >> 12) & 0x3FF}.{ApiVersion & 0xFFF}";

    public string DriverVersionText => $"0x{DriverVersion:X8}";

    public string VendorText => $"0x{VendorId:X4}";
    public string DeviceIdText => $"0x{DeviceId:X4}";

    public string Fp16Text => FormatSupport(SupportFp16Packed, SupportFp16Storage, SupportFp16Arithmetic);
    public string Int8Text => FormatSupport(SupportInt8Packed, SupportInt8Storage, SupportInt8Arithmetic);
    public string Int16Text => FormatSupport(SupportInt16Packed, SupportInt16Storage, SupportInt16Arithmetic);

    private static string FormatSupport(bool packed, bool storage, bool arithmetic)
    {
        var parts = new List<string>(3);
        if (packed) parts.Add("packed");
        if (storage) parts.Add("storage");
        if (arithmetic) parts.Add("arithmetic");
        return parts.Count == 0 ? "不支持" : string.Join(" / ", parts);
    }
}
