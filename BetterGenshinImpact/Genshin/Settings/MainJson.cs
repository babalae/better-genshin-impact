using System.Text.Json.Serialization;

namespace BetterGenshinImpact.Genshin.Settings;

public class MainJson
{
    [JsonPropertyName("deviceLanguageType")]
    public int DeviceLanguageType { get; set; }

    [JsonPropertyName("deviceVoiceLanguageType")]
    public int DeviceVoiceLanguageType { get; set; }

    [JsonPropertyName("inputData")]
    public string? InputData { get; set; }
}
