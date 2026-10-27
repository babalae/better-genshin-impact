using System;
using System.Globalization;
using BetterGenshinImpact.Core.Recognition.Ncnn;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     单套 ncnn 调优参数（检测 / 识别 / YOLO 各一份，互不影响）。
///     <para>
///         默认值与 <see cref="NcnnNetOptions" /> 保持一致（即 ncnn::Option 的默认构造值）；
///         启动准备阶段会尝试用 shim 的 <c>shim_get_default_options</c> 校正，旧版 DLL 时沿用这里的默认值。
///     </para>
///     <para>
///         fp16 / int8 / winograd / packing 必须在 <c>load_param</c> 之前写入 <c>net-&gt;opt</c>，
///         因此这些字段由 <see cref="ToNetOptions" /> 在创建会话时一次性下发。
///     </para>
/// </summary>
public partial class NcnnTuningSettings : ObservableObject
{
    public const int DefaultThreads = 4;
    public const int MaxThreads = 16;
    public const int MaxParallelSessions = 4;
    public const int DefaultOpenmpBlocktime = 20;

    /// <summary>CPU 线程数（vulkan 计算时仍用于 CPU 侧层与数据搬运）。</summary>
    [ObservableProperty] private int _threads = DefaultThreads;

    /// <summary>OpenMP 忙等时间（毫秒），-1 表示沿用 ncnn 默认。</summary>
    [ObservableProperty] private int _openmpBlocktime = DefaultOpenmpBlocktime;

    // ---------------- 精度 ----------------
    [ObservableProperty] private bool _useFp16Packed = true;
    [ObservableProperty] private bool _useFp16Storage = true;
    [ObservableProperty] private bool _useFp16Arithmetic = true;
    [ObservableProperty] private bool _useInt8Inference = true;
    [ObservableProperty] private bool _useInt8Packed = true;
    [ObservableProperty] private bool _useInt8Storage = true;
    [ObservableProperty] private bool _useInt8Arithmetic = true;
    [ObservableProperty] private bool _useBf16Storage;

    // ---------------- 卷积优化 ----------------
    [ObservableProperty] private bool _useWinogradConvolution = true;
    [ObservableProperty] private bool _useWinograd23 = true;
    [ObservableProperty] private bool _useWinograd43 = true;
    [ObservableProperty] private bool _useWinograd63 = true;
    [ObservableProperty] private bool _useSgemmConvolution = true;
    [ObservableProperty] private bool _usePackingLayout = true;

    // ---------------- Vulkan 细节 ----------------
    [ObservableProperty] private bool _useCooperativeMatrix = true;
    [ObservableProperty] private bool _useFp16Uniform = true;
    [ObservableProperty] private bool _useInt16Packed = true;
    [ObservableProperty] private bool _useShaderLocalMemory = true;
    [ObservableProperty] private bool _useA53A55OptimizedKernel;

    // ---------------- 并行 ----------------
    /// <summary>该用途的并行会话数（1 = 单会话顺序执行，保持历史行为）。</summary>
    [ObservableProperty] private int _parallelSessions = 1;

    /// <summary>
    ///     像素直输：把 8bit 像素缓冲直接交给 ncnn 的 <c>from_pixels</c>，省掉一次 CHW 浮点写入。
    ///     仅对「输入接受 0–255 且模型内自带归一化」的模型有意义；默认关闭以免改变既有数值语义。
    /// </summary>
    [ObservableProperty] private bool _usePixelInput;

    /// <summary>新建一份与 ncnn::Option 默认一致的参数。</summary>
    public static NcnnTuningSettings CreateDefault(int threads = DefaultThreads) => new()
    {
        Threads = Math.Clamp(threads, 1, MaxThreads)
    };

    /// <summary>从 shim 读到的真实默认值构造；<paramref name="options" /> 为 null 时回退内置默认。</summary>
    public static NcnnTuningSettings FromNetOptions(NcnnNetOptions? options, int fallbackThreads = DefaultThreads)
    {
        if (options == null) return CreateDefault(fallbackThreads);
        return new NcnnTuningSettings
        {
            Threads = options.Threads > 0 ? Math.Clamp(options.Threads, 1, MaxThreads) : Math.Clamp(fallbackThreads, 1, MaxThreads),
            OpenmpBlocktime = options.OpenmpBlocktime,
            UseFp16Packed = options.UseFp16Packed,
            UseFp16Storage = options.UseFp16Storage,
            UseFp16Arithmetic = options.UseFp16Arithmetic,
            UseInt8Inference = options.UseInt8Inference,
            UseInt8Packed = options.UseInt8Packed,
            UseInt8Storage = options.UseInt8Storage,
            UseInt8Arithmetic = options.UseInt8Arithmetic,
            UseBf16Storage = options.UseBf16Storage,
            UseWinogradConvolution = options.UseWinogradConvolution,
            UseWinograd23 = options.UseWinograd23,
            UseWinograd43 = options.UseWinograd43,
            UseWinograd63 = options.UseWinograd63,
            UseSgemmConvolution = options.UseSgemmConvolution,
            UsePackingLayout = options.UsePackingLayout,
            UseCooperativeMatrix = options.UseCooperativeMatrix,
            UseFp16Uniform = options.UseFp16Uniform,
            UseInt16Packed = options.UseInt16Packed,
            UseShaderLocalMemory = options.UseShaderLocalMemory,
            UseA53A55OptimizedKernel = options.UseA53A55OptimizedKernel
        };
    }

    /// <summary>深拷贝（配置对象可变，草稿与已保存值必须分离）。</summary>
    public NcnnTuningSettings Clone()
    {
        var copy = new NcnnTuningSettings();
        copy.CopyFrom(this);
        return copy;
    }

    /// <summary>把 <paramref name="other" /> 的全部字段写入当前实例（不替换引用，便于 WPF 绑定保持）。</summary>
    public void CopyFrom(NcnnTuningSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Threads = other.Threads;
        OpenmpBlocktime = other.OpenmpBlocktime;
        UseFp16Packed = other.UseFp16Packed;
        UseFp16Storage = other.UseFp16Storage;
        UseFp16Arithmetic = other.UseFp16Arithmetic;
        UseInt8Inference = other.UseInt8Inference;
        UseInt8Packed = other.UseInt8Packed;
        UseInt8Storage = other.UseInt8Storage;
        UseInt8Arithmetic = other.UseInt8Arithmetic;
        UseBf16Storage = other.UseBf16Storage;
        UseWinogradConvolution = other.UseWinogradConvolution;
        UseWinograd23 = other.UseWinograd23;
        UseWinograd43 = other.UseWinograd43;
        UseWinograd63 = other.UseWinograd63;
        UseSgemmConvolution = other.UseSgemmConvolution;
        UsePackingLayout = other.UsePackingLayout;
        UseCooperativeMatrix = other.UseCooperativeMatrix;
        UseFp16Uniform = other.UseFp16Uniform;
        UseInt16Packed = other.UseInt16Packed;
        UseShaderLocalMemory = other.UseShaderLocalMemory;
        UseA53A55OptimizedKernel = other.UseA53A55OptimizedKernel;
        ParallelSessions = other.ParallelSessions;
        UsePixelInput = other.UsePixelInput;
    }

    /// <summary>越界值修正（旧配置、手改 JSON 或核心数变化后调用）；返回自身便于链式使用。</summary>
    public NcnnTuningSettings Normalize()
    {
        Threads = Math.Clamp(Threads, 1, MaxThreads);
        OpenmpBlocktime = Math.Clamp(OpenmpBlocktime, -1, 1000);
        ParallelSessions = Math.Clamp(ParallelSessions, 1, MaxParallelSessions);
        return this;
    }

    public NcnnTuningSettings NormalizedCopy()
    {
        var copy = Clone();
        return copy.Normalize();
    }

    /// <summary>只比较是否变化用的稳定签名（不参与验证指纹；派生属性，不参与 JSON 往返）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string SignatureSuffix => string.Create(CultureInfo.InvariantCulture,
        $"|{Threads}|{OpenmpBlocktime}|{B(UseFp16Packed)}{B(UseFp16Storage)}{B(UseFp16Arithmetic)}|" +
        $"{B(UseInt8Inference)}{B(UseInt8Packed)}{B(UseInt8Storage)}{B(UseInt8Arithmetic)}|{B(UseBf16Storage)}|" +
        $"{B(UseWinogradConvolution)}{B(UseWinograd23)}{B(UseWinograd43)}{B(UseWinograd63)}{B(UseSgemmConvolution)}|" +
        $"{B(UsePackingLayout)}{B(UseCooperativeMatrix)}{B(UseFp16Uniform)}{B(UseInt16Packed)}" +
        $"{B(UseShaderLocalMemory)}{B(UseA53A55OptimizedKernel)}|{ParallelSessions}|{B(UsePixelInput)}");

    private static int B(bool value) => value ? 1 : 0;

    public bool Equals(NcnnTuningSettings? other) => other != null && SignatureSuffix == other.SignatureSuffix;

    public override bool Equals(object? obj) => obj is NcnnTuningSettings other && Equals(other);

    public override int GetHashCode() => SignatureSuffix.GetHashCode(StringComparison.Ordinal);

    /// <summary>把本套参数展开成 shim 的版本化选项结构（路径与设备由调用方补）。</summary>
    public NcnnNetOptions ToNetOptions(string paramPath, string modelPath, int gpuIndex, string? paramMemory = null) => new()
    {
        ParamPath = paramPath,
        ModelPath = modelPath,
        ParamMemory = paramMemory,
        GpuIndex = gpuIndex,
        Threads = Math.Clamp(Threads, 1, MaxThreads),
        OpenmpBlocktime = OpenmpBlocktime,
        UseWinogradConvolution = UseWinogradConvolution,
        UseSgemmConvolution = UseSgemmConvolution,
        UseInt8Inference = UseInt8Inference,
        UseBf16Storage = UseBf16Storage,
        UseFp16Packed = UseFp16Packed,
        UseFp16Storage = UseFp16Storage,
        UseFp16Arithmetic = UseFp16Arithmetic,
        UseInt8Packed = UseInt8Packed,
        UseInt8Storage = UseInt8Storage,
        UseInt8Arithmetic = UseInt8Arithmetic,
        UsePackingLayout = UsePackingLayout,
        UseCooperativeMatrix = UseCooperativeMatrix,
        UseWinograd23 = UseWinograd23,
        UseWinograd43 = UseWinograd43,
        UseWinograd63 = UseWinograd63,
        UseA53A55OptimizedKernel = UseA53A55OptimizedKernel,
        UseFp16Uniform = UseFp16Uniform,
        UseInt16Packed = UseInt16Packed,
        UseShaderLocalMemory = UseShaderLocalMemory,
        // 不随 UI 暴露的项固定为 ncnn::Option 的默认值：bf16 packed 在部分设备上会让
        // cooperative matrix 着色器编译失败（B580 + ncnn 20261020 实测崩溃），必须显式关闭。
        UseBf16Packed = false,
        UseInt16Storage = true,
        UseInt8Uniform = true,
        UseLocalPoolAllocator = true,
        FlushDenormals = 3
    };

    /// <summary>
    ///     把「仍然是内置默认值」的字段校正为 shim 报告的真实 ncnn 默认值，用户改动过的字段保持不动。
    ///     内置常量一旦与库版本漂移，就会改变着色器/算子选择（历史上 bf16 packed 的漂移导致 Vulkan 崩溃），
    ///     因此启动准备阶段用 <c>shim_get_default_options</c> 对齐；线程数属于产品默认，不参与对齐。
    /// </summary>
    public void AlignWithDefaults(NcnnTuningSettings shimDefaults)
    {
        ArgumentNullException.ThrowIfNull(shimDefaults);
        var builtIn = CreateDefault();
        if (OpenmpBlocktime == builtIn.OpenmpBlocktime) OpenmpBlocktime = shimDefaults.OpenmpBlocktime;
        if (UseFp16Packed == builtIn.UseFp16Packed) UseFp16Packed = shimDefaults.UseFp16Packed;
        if (UseFp16Storage == builtIn.UseFp16Storage) UseFp16Storage = shimDefaults.UseFp16Storage;
        if (UseFp16Arithmetic == builtIn.UseFp16Arithmetic) UseFp16Arithmetic = shimDefaults.UseFp16Arithmetic;
        if (UseInt8Inference == builtIn.UseInt8Inference) UseInt8Inference = shimDefaults.UseInt8Inference;
        if (UseInt8Packed == builtIn.UseInt8Packed) UseInt8Packed = shimDefaults.UseInt8Packed;
        if (UseInt8Storage == builtIn.UseInt8Storage) UseInt8Storage = shimDefaults.UseInt8Storage;
        if (UseInt8Arithmetic == builtIn.UseInt8Arithmetic) UseInt8Arithmetic = shimDefaults.UseInt8Arithmetic;
        if (UseBf16Storage == builtIn.UseBf16Storage) UseBf16Storage = shimDefaults.UseBf16Storage;
        if (UseWinogradConvolution == builtIn.UseWinogradConvolution)
            UseWinogradConvolution = shimDefaults.UseWinogradConvolution;
        if (UseWinograd23 == builtIn.UseWinograd23) UseWinograd23 = shimDefaults.UseWinograd23;
        if (UseWinograd43 == builtIn.UseWinograd43) UseWinograd43 = shimDefaults.UseWinograd43;
        if (UseWinograd63 == builtIn.UseWinograd63) UseWinograd63 = shimDefaults.UseWinograd63;
        if (UseSgemmConvolution == builtIn.UseSgemmConvolution) UseSgemmConvolution = shimDefaults.UseSgemmConvolution;
        if (UsePackingLayout == builtIn.UsePackingLayout) UsePackingLayout = shimDefaults.UsePackingLayout;
        if (UseCooperativeMatrix == builtIn.UseCooperativeMatrix)
            UseCooperativeMatrix = shimDefaults.UseCooperativeMatrix;
        if (UseFp16Uniform == builtIn.UseFp16Uniform) UseFp16Uniform = shimDefaults.UseFp16Uniform;
        if (UseInt16Packed == builtIn.UseInt16Packed) UseInt16Packed = shimDefaults.UseInt16Packed;
        if (UseShaderLocalMemory == builtIn.UseShaderLocalMemory)
            UseShaderLocalMemory = shimDefaults.UseShaderLocalMemory;
        if (UseA53A55OptimizedKernel == builtIn.UseA53A55OptimizedKernel)
            UseA53A55OptimizedKernel = shimDefaults.UseA53A55OptimizedKernel;
    }
}
