using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Tavern.Model;

internal sealed class KongyingTavernResponse<T>
{
    [JsonProperty("error")]
    public bool Error { get; set; }

    [JsonProperty("errorStatus")]
    public int ErrorStatus { get; set; }

    [JsonProperty("errorData")]
    public string? ErrorData { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("data")]
    public T? Data { get; set; }

    [JsonProperty("users")]
    public JToken? Users { get; set; }

    [JsonProperty("time")]
    public string? Time { get; set; }
}

internal sealed class ListPageBinMd5Item
{
    [JsonProperty("md5")]
    public string Md5 { get; set; } = string.Empty;

    [JsonProperty("time")]
    public long Time { get; set; }
}
