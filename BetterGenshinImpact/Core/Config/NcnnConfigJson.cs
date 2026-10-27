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
        DetTuning = value.DetTuning,
        RecTuning = value.RecTuning,
        YoloTuning = value.YoloTuning
    };
}

/// <summary>
///     嵌套 <c>ncnn</c> 节点的白名单载体；调优参数缺失时回落默认，其余字段缺失沿用各字段默认值。
///     <para>
///         三套调优由 <see cref="NcnnTuningSettings" /> 直接 JSON 往返：属性名与主仓库 CamelCase
///         命名策略一致，JSON 形状与旧白名单 DTO 兼容；未来给 <see cref="NcnnTuningSettings" />
///         加纯展示 public 属性时需 <c>[JsonIgnore]</c>，避免被写进配置文件。
///     </para>
/// </summary>
internal sealed class SerializedNcnnConfig
{
    public InferenceBackend? PreferredBackend { get; set; }
    public bool UseOcr { get; set; }
    public bool UseYolo { get; set; }
    public NcnnComputeMode? ComputeMode { get; set; }
    public int? GpuIndex { get; set; }
    public NcnnTuningSettings? DetTuning { get; set; }
    public NcnnTuningSettings? RecTuning { get; set; }
    public NcnnTuningSettings? YoloTuning { get; set; }

    public NcnnConfig ToConfig()
    {
        return new NcnnConfig
        {
            PreferredBackend = PreferredBackend ?? InferenceBackend.Onnx,
            UseOcr = UseOcr,
            UseYolo = UseYolo,
            ComputeMode = ComputeMode ?? global::BetterGenshinImpact.Core.Config.NcnnComputeMode.Vulkan,
            GpuIndex = GpuIndex ?? 0,
            DetTuning = DetTuning is null ? NcnnTuningSettings.CreateDefault() : DetTuning.Normalize(),
            RecTuning = RecTuning is null ? NcnnTuningSettings.CreateDefault() : RecTuning.Normalize(),
            YoloTuning = YoloTuning is null ? NcnnTuningSettings.CreateDefault() : YoloTuning.Normalize()
        };
    }
}
