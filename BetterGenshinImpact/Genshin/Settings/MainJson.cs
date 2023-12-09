using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Genshin.Settings;

public sealed class MainJson
{
    [JsonPropertyName("deviceLanguageType")]
    public int DeviceLanguageType { get; set; }

    [JsonPropertyName("deviceVoiceLanguageType")]
    public int DeviceVoiceLanguageType { get; set; }

    [JsonPropertyName("inputData")]
    public string? InputData { get; set; }

    [JsonPropertyName("_overrideControllerMapKeyList")]
    public string[]? OverrideControllerMapKeyList { get; set; }

    [JsonPropertyName("_overrideControllerMapValueList")]
    public string[]? OverrideControllerMapValueList { get; set; }
}
