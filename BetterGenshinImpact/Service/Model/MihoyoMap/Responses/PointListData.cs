using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Model.MihoyoMap.Responses
{
    public class PointListData
    {
        [JsonProperty("point_list")] public List<PointItem> PointList { get; set; } = new();
        [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
    }
}
