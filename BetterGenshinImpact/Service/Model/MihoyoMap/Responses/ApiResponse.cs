using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Model.MihoyoMap.Responses
{
    public class ApiResponse<T>
    {
        [JsonProperty("retcode")] public int Retcode { get; set; }
        [JsonProperty("message")] public string Message { get; set; } = string.Empty;
        [JsonProperty("data")] public T Data { get; set; } = default!;
        [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
    }
}
