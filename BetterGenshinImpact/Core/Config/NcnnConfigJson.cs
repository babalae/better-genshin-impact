using System.Text.Json;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     ncnn 配置的 JSON 读取：嵌套节点缺失或损坏时回落默认配置。
///     从 <see cref="HardwareAccelerationConfigJsonConverter" /> 独立，保持 ncnn 序列化细节与主转换器解耦。
///     写路径由主转换器直接序列化 <see cref="NcnnConfig" />：其 public 属性即白名单
///     （后端意图、逐用途开关、计算模式、设备索引与三套调优），无多余字段、无循环引用。
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
                // 直接反序列化持久化对象：枚举越界由 NcnnConfig 的 OnPreferredBackendChanged /
                // OnComputeModeChanged 兜底回落，缺失字段保持属性初始化器默认。
                var config = nested.Deserialize<NcnnConfig>(options) ?? new NcnnConfig();
                // 调优参数节点缺失时属性为 null，读路径统一补默认（FromConfig 侧的 null 保护继续兜底）。
                config.DetTuning ??= NcnnTuningSettings.CreateDefault();
                config.RecTuning ??= NcnnTuningSettings.CreateDefault();
                config.YoloTuning ??= NcnnTuningSettings.CreateDefault();
                return config;
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
}
