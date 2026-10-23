using System.Text.Json;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     ncnn 配置的 JSON 读写：白名单 DTO + 旧平铺结构迁移。
///     从 <see cref="HardwareAccelerationConfigJsonConverter" /> 独立，保持 ncnn 序列化细节与主转换器解耦。
/// </summary>
internal static class NcnnConfigJson
{
    /// <summary>
    ///     从 hardwareAcceleration 节点读取 ncnn 配置：优先读嵌套 <c>ncnn</c> 节点；
    ///     节点缺失时迁移旧平铺字段（ncnn 独立化前写入的 config.json），缺失字段回落默认值。
    /// </summary>
    public static NcnnConfig Read(JsonElement hardwareRoot, JsonSerializerOptions options)
    {
        if (hardwareRoot.ValueKind == JsonValueKind.Object
            && (hardwareRoot.TryGetProperty("ncnn", out var nested) || hardwareRoot.TryGetProperty("Ncnn", out nested)))
        {
            try
            {
                return nested.Deserialize<SerializedNcnnConfig>(options)?.ToConfig() ?? new NcnnConfig();
            }
            catch (JsonException)
            {
                // 嵌套节点损坏只回落默认 ncnn 配置，不影响其他字段的读取。
                return new NcnnConfig();
            }
        }

        var legacy = hardwareRoot.Deserialize<LegacyNcnnFields>(options);
        return legacy?.ToConfig() ?? new NcnnConfig();
    }

    /// <summary>把持久化对象转成嵌套节点的白名单 DTO，供主转换器序列化。</summary>
    public static SerializedNcnnConfig ToDto(NcnnConfig value) => new()
    {
        PreferredBackend = value.PreferredBackend,
        UseOcr = value.UseOcr,
        UseYolo = value.UseYolo,
        ComputeMode = value.ComputeMode,
        GpuIndex = value.GpuIndex,
        OcrUseBuckets = value.OcrUseBuckets,
        Threads = value.Threads,
        DetTuning = SerializedNcnnTuning.From(value.DetTuning ?? NcnnTuningSettings.CreateDefault()),
        RecTuning = SerializedNcnnTuning.From(value.RecTuning ?? NcnnTuningSettings.CreateDefault()),
        YoloTuning = SerializedNcnnTuning.From(value.YoloTuning ?? NcnnTuningSettings.CreateDefault())
    };
}

/// <summary>嵌套 <c>ncnn</c> 节点的白名单载体；调优参数缺失时回落默认，其余字段缺失沿用各字段默认值。</summary>
internal sealed class SerializedNcnnConfig
{
    public InferenceBackend? PreferredBackend { get; set; }
    public bool UseOcr { get; set; }
    public bool UseYolo { get; set; }
    public NcnnComputeMode? ComputeMode { get; set; }
    public int? GpuIndex { get; set; }
    public bool? OcrUseBuckets { get; set; }
    public int? Threads { get; set; }
    public SerializedNcnnTuning? DetTuning { get; set; }
    public SerializedNcnnTuning? RecTuning { get; set; }
    public SerializedNcnnTuning? YoloTuning { get; set; }

    public NcnnConfig ToConfig()
    {
        var fallbackThreads = Threads is >= 1 and <= NcnnTuningSettings.MaxThreads
            ? Threads.Value
            : NcnnBackendSettings.DefaultThreads;
        return new NcnnConfig
        {
            PreferredBackend = PreferredBackend ?? InferenceBackend.Onnx,
            UseOcr = UseOcr,
            UseYolo = UseYolo,
            ComputeMode = ComputeMode ?? global::BetterGenshinImpact.Core.Config.NcnnComputeMode.Vulkan,
            GpuIndex = GpuIndex ?? 0,
            OcrUseBuckets = OcrUseBuckets ?? true,
            Threads = fallbackThreads,
            DetTuning = DetTuning?.ToSettings(fallbackThreads) ?? NcnnTuningSettings.CreateDefault(fallbackThreads),
            RecTuning = RecTuning?.ToSettings(fallbackThreads) ?? NcnnTuningSettings.CreateDefault(fallbackThreads),
            YoloTuning = YoloTuning?.ToSettings(fallbackThreads) ?? NcnnTuningSettings.CreateDefault(fallbackThreads)
        };
    }
}

/// <summary>
///     ncnn 独立化前的旧平铺字段（写在 hardwareAcceleration 根节点上），仅用于读迁移，不再写出。
/// </summary>
internal sealed class LegacyNcnnFields
{
    public InferenceBackend? PreferredBackend { get; set; }
    public bool UseNcnnOcr { get; set; }
    public bool UseNcnnYolo { get; set; }
    public NcnnComputeMode? NcnnComputeMode { get; set; }
    public int? NcnnGpuIndex { get; set; }
    public int? NcnnThreads { get; set; }
    public SerializedNcnnTuning? NcnnDetTuning { get; set; }
    public SerializedNcnnTuning? NcnnRecTuning { get; set; }
    public SerializedNcnnTuning? NcnnYoloTuning { get; set; }

    public NcnnConfig ToConfig()
    {
        // 旧配置没有三套调优参数：线程数回落到历史单一字段，其余字段回落到 ncnn::Option 默认。
        var fallbackThreads = NcnnThreads is >= 1 and <= NcnnTuningSettings.MaxThreads
            ? NcnnThreads.Value
            : NcnnBackendSettings.DefaultThreads;
        return new NcnnConfig
        {
            // 旧配置没有全局后端字段：只有 OCR 与 YOLO 都走 ncnn 才视为全局 ncnn，混合状态保持单项覆盖语义。
            PreferredBackend = PreferredBackend
                               ?? (UseNcnnOcr && UseNcnnYolo ? InferenceBackend.Ncnn : InferenceBackend.Onnx),
            UseOcr = UseNcnnOcr,
            UseYolo = UseNcnnYolo,
            ComputeMode = NcnnComputeMode ?? global::BetterGenshinImpact.Core.Config.NcnnComputeMode.Vulkan,
            GpuIndex = NcnnGpuIndex ?? 0,
            Threads = fallbackThreads,
            DetTuning = NcnnDetTuning?.ToSettings(fallbackThreads) ?? NcnnTuningSettings.CreateDefault(fallbackThreads),
            RecTuning = NcnnRecTuning?.ToSettings(fallbackThreads) ?? NcnnTuningSettings.CreateDefault(fallbackThreads),
            YoloTuning = NcnnYoloTuning?.ToSettings(fallbackThreads) ?? NcnnTuningSettings.CreateDefault(fallbackThreads)
        };
    }
}

/// <summary>
///     单套调优参数的白名单载体：全部字段可空，缺失即回落默认。
///     逐字段读写而不是整对象序列化，避免 <see cref="NcnnTuningSettings" /> 的生成属性或新增字段被静默丢弃。
/// </summary>
internal sealed class SerializedNcnnTuning
{
    public int? Threads { get; set; }
    public int? OpenmpBlocktime { get; set; }
    public bool? UseFp16Packed { get; set; }
    public bool? UseFp16Storage { get; set; }
    public bool? UseFp16Arithmetic { get; set; }
    public bool? UseInt8Inference { get; set; }
    public bool? UseInt8Packed { get; set; }
    public bool? UseInt8Storage { get; set; }
    public bool? UseInt8Arithmetic { get; set; }
    public bool? UseBf16Storage { get; set; }
    public bool? UseWinogradConvolution { get; set; }
    public bool? UseWinograd23 { get; set; }
    public bool? UseWinograd43 { get; set; }
    public bool? UseWinograd63 { get; set; }
    public bool? UseSgemmConvolution { get; set; }
    public bool? UsePackingLayout { get; set; }
    public bool? UseCooperativeMatrix { get; set; }
    public bool? UseFp16Uniform { get; set; }
    public bool? UseInt16Packed { get; set; }
    public bool? UseShaderLocalMemory { get; set; }
    public bool? UseA53A55OptimizedKernel { get; set; }
    public int? ParallelSessions { get; set; }
    public bool? UsePixelInput { get; set; }

    public NcnnTuningSettings ToSettings(int fallbackThreads)
    {
        var settings = NcnnTuningSettings.CreateDefault(fallbackThreads);
        if (Threads is { } threads) settings.Threads = threads;
        if (OpenmpBlocktime is { } blocktime) settings.OpenmpBlocktime = blocktime;
        if (UseFp16Packed is { } fp16Packed) settings.UseFp16Packed = fp16Packed;
        if (UseFp16Storage is { } fp16Storage) settings.UseFp16Storage = fp16Storage;
        if (UseFp16Arithmetic is { } fp16Arithmetic) settings.UseFp16Arithmetic = fp16Arithmetic;
        if (UseInt8Inference is { } int8Inference) settings.UseInt8Inference = int8Inference;
        if (UseInt8Packed is { } int8Packed) settings.UseInt8Packed = int8Packed;
        if (UseInt8Storage is { } int8Storage) settings.UseInt8Storage = int8Storage;
        if (UseInt8Arithmetic is { } int8Arithmetic) settings.UseInt8Arithmetic = int8Arithmetic;
        if (UseBf16Storage is { } bf16Storage) settings.UseBf16Storage = bf16Storage;
        if (UseWinogradConvolution is { } winograd) settings.UseWinogradConvolution = winograd;
        if (UseWinograd23 is { } w23) settings.UseWinograd23 = w23;
        if (UseWinograd43 is { } w43) settings.UseWinograd43 = w43;
        if (UseWinograd63 is { } w63) settings.UseWinograd63 = w63;
        if (UseSgemmConvolution is { } sgemm) settings.UseSgemmConvolution = sgemm;
        if (UsePackingLayout is { } packing) settings.UsePackingLayout = packing;
        if (UseCooperativeMatrix is { } cooperative) settings.UseCooperativeMatrix = cooperative;
        if (UseFp16Uniform is { } fp16Uniform) settings.UseFp16Uniform = fp16Uniform;
        if (UseInt16Packed is { } int16Packed) settings.UseInt16Packed = int16Packed;
        if (UseShaderLocalMemory is { } shaderLocal) settings.UseShaderLocalMemory = shaderLocal;
        if (UseA53A55OptimizedKernel is { } a53a55) settings.UseA53A55OptimizedKernel = a53a55;
        if (ParallelSessions is { } sessions) settings.ParallelSessions = sessions;
        if (UsePixelInput is { } pixelInput) settings.UsePixelInput = pixelInput;
        return settings.Normalize();
    }

    public static SerializedNcnnTuning From(NcnnTuningSettings settings) => new()
    {
        Threads = settings.Threads,
        OpenmpBlocktime = settings.OpenmpBlocktime,
        UseFp16Packed = settings.UseFp16Packed,
        UseFp16Storage = settings.UseFp16Storage,
        UseFp16Arithmetic = settings.UseFp16Arithmetic,
        UseInt8Inference = settings.UseInt8Inference,
        UseInt8Packed = settings.UseInt8Packed,
        UseInt8Storage = settings.UseInt8Storage,
        UseInt8Arithmetic = settings.UseInt8Arithmetic,
        UseBf16Storage = settings.UseBf16Storage,
        UseWinogradConvolution = settings.UseWinogradConvolution,
        UseWinograd23 = settings.UseWinograd23,
        UseWinograd43 = settings.UseWinograd43,
        UseWinograd63 = settings.UseWinograd63,
        UseSgemmConvolution = settings.UseSgemmConvolution,
        UsePackingLayout = settings.UsePackingLayout,
        UseCooperativeMatrix = settings.UseCooperativeMatrix,
        UseFp16Uniform = settings.UseFp16Uniform,
        UseInt16Packed = settings.UseInt16Packed,
        UseShaderLocalMemory = settings.UseShaderLocalMemory,
        UseA53A55OptimizedKernel = settings.UseA53A55OptimizedKernel,
        ParallelSessions = settings.ParallelSessions,
        UsePixelInput = settings.UsePixelInput
    };
}
