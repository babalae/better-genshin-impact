using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Model.MihoyoMap.Requests
{
    public class PointListRequest
    {
        [JsonProperty("map_id")] public int MapId { get; set; } = 2;
        [JsonProperty("app_sn")] public string AppSn { get; set; } = "ys_obc";
        [JsonProperty("lang")] public string Lang { get; set; } = "zh-cn";
        [JsonProperty("label_ids")] public List<int> LabelIds { get; set; } = new();
    }
}
