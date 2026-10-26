using System.Text.Json;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     ncnn 配置的 JSON 读写：白名单 DTO，嵌套节点缺失或损坏时回落默认配置。
///     从 <see cref="HardwareAccelerationConfigJsonConverter" /> 独立，保持 ncnn 序列化细节与主转换器解耦。
/// </summary>
internal static class NcnnConfigJson
{
    /// <summary>
    ///     从 hardwareAcceleration 节点读取 ncnn 配置：优先读嵌套 <c>ncnn</c> 节点；
    ///     节点缺失或损坏时回落默认 ncnn 配置（ncnn 为未发布分支引入的实验后端，无历史用户需要迁移）。
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

        // 嵌套节点缺失/损坏：ncnn 是未发布分支引入的实验后端，直接回落默认配置
        return new NcnnConfig();
    }

    /// <summary>把持久化对象转成嵌套节点的白名单 DTO，供主转换器序列化。</summary>
    public static SerializedNcnnConfig ToDto(NcnnConfig value) => new()
    {
        PreferredBackend = value.PreferredBackend,
        UseOcr = value.UseOcr,
        UseYolo = value.UseYolo,
        ComputeMode = value.ComputeMode,
        GpuIndex = value.GpuIndex,
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
    public SerializedNcnnTuning? DetTuning { get; set; }
    public SerializedNcnnTuning? RecTuning { get; set; }
    public SerializedNcnnTuning? YoloTuning { get; set; }

    public NcnnConfig ToConfig()
    {
        return new NcnnConfig
        {
            PreferredBackend = PreferredBackend ?? InferenceBackend.Onnx,
            UseOcr = UseOcr,
            UseYolo = UseYolo,
            ComputeMode = ComputeMode ?? global::BetterGenshinImpact.Core.Config.NcnnComputeMode.Vulkan,
            GpuIndex = GpuIndex ?? 0,
            DetTuning = DetTuning?.ToSettings(NcnnBackendSettings.DefaultThreads) ?? NcnnTuningSettings.CreateDefault(),
            RecTuning = RecTuning?.ToSettings(NcnnBackendSettings.DefaultThreads) ?? NcnnTuningSettings.CreateDefault(),
            YoloTuning = YoloTuning?.ToSettings(NcnnBackendSettings.DefaultThreads) ?? NcnnTuningSettings.CreateDefault()
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
