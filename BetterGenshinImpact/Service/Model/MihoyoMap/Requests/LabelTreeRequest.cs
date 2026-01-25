using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Model.MihoyoMap.Requests
{
    public class LabelTreeRequest
    {
        [JsonProperty("map_id")] public int MapId { get; set; } = 2;
        [JsonProperty("app_sn")] public string AppSn { get; set; } = "ys_obc";
        [JsonProperty("lang")] public string Lang { get; set; } = "zh-cn";
    }
}
