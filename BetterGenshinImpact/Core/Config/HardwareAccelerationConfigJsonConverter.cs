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
            return new()
            {
                FormatVersion = data.FormatVersion, InferenceDevice = data.InferenceDevice, CpuOcr = data.CpuOcr,
                // 全局后端意图：越界值由 PreferredBackend 的属性兜底回落 Onnx（对象初始化器走生成属性）。
                PreferredBackend = data.PreferredBackend,
                // 后端子配置独立读取：优先嵌套节点，缺失时回落默认（细节见 NcnnConfigJson / OpenVinoConfigJson）。
                Ncnn = NcnnConfigJson.Read(document.RootElement, options),
                OpenVino = OpenVinoConfigJson.Read(document.RootElement, options),
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
            // 后端子配置直接序列化持久化对象：public 属性即嵌套节点白名单（无循环，派生属性已 [JsonIgnore]）。
            PreferredBackend = value.PreferredBackend,
            Ncnn = value.Ncnn, OpenVino = value.OpenVino,
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
        public InferenceBackend PreferredBackend { get; set; }
        public NcnnConfig? Ncnn { get; set; }
        public OpenVinoConfig? OpenVino { get; set; }
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
}
