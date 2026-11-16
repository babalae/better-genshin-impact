using System.Text.Json;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     OpenVINO 后端配置的 JSON 读取：嵌套节点缺失或损坏时回落默认配置。
///     与 <see cref="NcnnConfigJson" /> 同构，保持各后端序列化细节与主转换器解耦。
///     写路径由主转换器直接序列化 <see cref="OpenVinoConfig" />。
/// </summary>
internal static class OpenVinoConfigJson
{
    public static OpenVinoConfig Read(JsonElement hardwareRoot, JsonSerializerOptions options)
    {
        if (hardwareRoot.ValueKind == JsonValueKind.Object
            && (hardwareRoot.TryGetProperty("openVino", out var nested) || hardwareRoot.TryGetProperty("OpenVino", out nested)))
        {
            try
            {
                var config = nested.Deserialize<OpenVinoConfig>(options) ?? new OpenVinoConfig();
                if (string.IsNullOrWhiteSpace(config.DeviceId)) config.DeviceId = "CPU";
                return config;
            }
            catch (JsonException)
            {
                // 嵌套节点损坏只回落默认 OpenVINO 配置，不影响其他字段的读取。
                return new OpenVinoConfig();
            }
        }

        return new OpenVinoConfig();
    }
}
