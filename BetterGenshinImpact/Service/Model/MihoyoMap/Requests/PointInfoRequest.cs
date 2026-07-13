using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Model.MihoyoMap.Requests
{
    public class PointInfoRequest
    {
        [JsonProperty("map_id")] public int MapId { get; set; } = 2;
        [JsonProperty("app_sn")] public string AppSn { get; set; } = "ys_obc";
        [JsonProperty("lang")] public string Lang { get; set; } = "zh-cn";
        [JsonProperty("point_id")] public int PointId { get; set; }
    }
}
