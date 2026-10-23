using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetterGenshinImpact.Core.Recognition.ONNX;

namespace BetterGenshinImpact.Core.Config;

/// <summary>把无效推理字段隔离在硬件设置内，不能因为单个字段错误重置整份用户配置。</summary>
public sealed class HardwareAccelerationConfigJsonConverter : JsonConverter<HardwareAccelerationConfig>
{
    public override HardwareAccelerationConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        try
        {
            var data = document.RootElement.Deserialize<SerializedConfiguration>(options);
            if (data == null) return new();
            var useNcnnOcr = data.UseNcnnOcr;
            var useNcnnYolo = data.UseNcnnYolo;
            // 旧配置没有三套调优参数：线程数回落到历史单一字段，其余字段回落到 ncnn::Option 默认。
            var fallbackThreads = data.NcnnThreads is >= 1 and <= NcnnTuningSettings.MaxThreads
                ? data.NcnnThreads.Value
                : NcnnBackendSettings.DefaultThreads;
            return new()
            {
                FormatVersion = data.FormatVersion, InferenceDevice = data.InferenceDevice, CpuOcr = data.CpuOcr,
                // 旧配置没有全局后端字段：只有 OCR 与 YOLO 都走 ncnn 才视为全局 ncnn，混合状态保持单项覆盖语义。
                PreferredBackend = data.PreferredBackend
                    ?? (useNcnnOcr && useNcnnYolo ? InferenceBackend.Ncnn : InferenceBackend.Onnx),
                UseNcnnOcr = useNcnnOcr, UseNcnnYolo = useNcnnYolo,
                NcnnComputeMode = data.NcnnComputeMode ?? NcnnComputeMode.Vulkan,
                NcnnGpuIndex = data.NcnnGpuIndex ?? 0,
                NcnnThreads = fallbackThreads,
                NcnnDetTuning = data.NcnnDetTuning?.ToSettings(fallbackThreads)
                    ?? NcnnTuningSettings.CreateDefault(fallbackThreads),
                NcnnRecTuning = data.NcnnRecTuning?.ToSettings(fallbackThreads)
                    ?? NcnnTuningSettings.CreateDefault(fallbackThreads),
                NcnnYoloTuning = data.NcnnYoloTuning?.ToSettings(fallbackThreads)
                    ?? NcnnTuningSettings.CreateDefault(fallbackThreads),
                OpenCvThreads = data.OpenCvThreads, OpenCvIdleSleep = data.OpenCvIdleSleep,
                Cpu = data.Cpu!, DirectML = data.DirectML!, OpenVINO = data.OpenVINO!, Cuda = data.Cuda!, TensorRT = data.TensorRT!,
                PaddleOcrCpu = data.PaddleOcrCpu ?? new(),
                ValidationState = data.ValidationState, VerifiedSignature = data.VerifiedSignature ?? "",
                ValidatedEngines = data.ValidatedEngines ?? [],
                LastFailedEngine = data.LastFailedEngine, LastValidationError = data.LastValidationError ?? ""
            };
        }
        catch (JsonException) { return new(); }
    }

    public override void Write(Utf8JsonWriter writer, HardwareAccelerationConfig value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new SerializedConfiguration
        {
            FormatVersion = value.FormatVersion, InferenceDevice = value.InferenceDevice, CpuOcr = value.CpuOcr,
            PreferredBackend = value.PreferredBackend,
            UseNcnnOcr = value.UseNcnnOcr, UseNcnnYolo = value.UseNcnnYolo,
            NcnnComputeMode = value.NcnnComputeMode,
            NcnnGpuIndex = value.NcnnGpuIndex, NcnnThreads = value.NcnnThreads,
            NcnnDetTuning = SerializedNcnnTuning.From(value.NcnnDetTuning ?? NcnnTuningSettings.CreateDefault()),
            NcnnRecTuning = SerializedNcnnTuning.From(value.NcnnRecTuning ?? NcnnTuningSettings.CreateDefault()),
            NcnnYoloTuning = SerializedNcnnTuning.From(value.NcnnYoloTuning ?? NcnnTuningSettings.CreateDefault()),
            OpenCvThreads = value.OpenCvThreads, OpenCvIdleSleep = value.OpenCvIdleSleep,
            Cpu = value.Cpu, DirectML = value.DirectML, OpenVINO = value.OpenVINO, Cuda = value.Cuda, TensorRT = value.TensorRT,
            PaddleOcrCpu = value.PaddleOcrCpu,
            ValidationState = value.ValidationState, VerifiedSignature = value.VerifiedSignature,
            ValidatedEngines = value.ValidatedEngines,
            LastFailedEngine = value.LastFailedEngine, LastValidationError = value.LastValidationError
        }, options);

    private sealed class SerializedConfiguration
    {
        public SerializedConfiguration() { }
        public int FormatVersion { get; set; }
        public InferenceDeviceType InferenceDevice { get; set; }
        public bool CpuOcr { get; set; } = true;
        public InferenceBackend? PreferredBackend { get; set; }
        public bool UseNcnnOcr { get; set; }
        public bool UseNcnnYolo { get; set; }
        public NcnnComputeMode? NcnnComputeMode { get; set; }
        public int? NcnnGpuIndex { get; set; }
        public int? NcnnThreads { get; set; }
        public SerializedNcnnTuning? NcnnDetTuning { get; set; }
        public SerializedNcnnTuning? NcnnRecTuning { get; set; }
        public SerializedNcnnTuning? NcnnYoloTuning { get; set; }
        public int OpenCvThreads { get; set; }
        public bool OpenCvIdleSleep { get; set; }
        public InferenceEngineSettings? Cpu { get; set; }
        public PaddleOcrCpuConfig? PaddleOcrCpu { get; set; }
        public InferenceEngineSettings? DirectML { get; set; }
        public InferenceEngineSettings? OpenVINO { get; set; }
        public InferenceEngineSettings? Cuda { get; set; }
        public InferenceEngineSettings? TensorRT { get; set; }
        public InferenceValidationState ValidationState { get; set; }
        public string? VerifiedSignature { get; set; }
        public InferenceDeviceType[]? ValidatedEngines { get; set; }
        public InferenceDeviceType? LastFailedEngine { get; set; }
        public string? LastValidationError { get; set; }
    }

    /// <summary>
    ///     单套调优参数的白名单载体：全部字段可空，缺失即回落默认。
    ///     逐字段读写而不是整对象序列化，避免 <see cref="NcnnTuningSettings" /> 的生成属性或新增字段被静默丢弃。
    /// </summary>
    private sealed class SerializedNcnnTuning
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
}
