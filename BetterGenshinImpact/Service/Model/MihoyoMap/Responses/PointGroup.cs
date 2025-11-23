using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Model.MihoyoMap.Responses
{
    public class PointGroup
    {
        [JsonProperty("group_id")] public int GroupId { get; set; }
        [JsonProperty("floor_id")] public int FloorId { get; set; }
    }
}
