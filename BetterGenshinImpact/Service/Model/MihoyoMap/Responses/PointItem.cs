using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Model.MihoyoMap.Responses
{
    public class PointItem
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("label_id")] public int LabelId { get; set; }
        [JsonProperty("x_pos")] public double XPos { get; set; }
        [JsonProperty("y_pos")] public double YPos { get; set; }
        [JsonProperty("author_name")] public string AuthorName { get; set; } = string.Empty;
        [JsonProperty("ctime")] public string Ctime { get; set; } = string.Empty;
        [JsonProperty("display_state")] public int DisplayState { get; set; }
        [JsonProperty("area_id")] public int AreaId { get; set; }
        [JsonProperty("ext_attrs")] public string ExtAttrs { get; set; } = string.Empty;
        [JsonProperty("ext_attrs_map")] public Dictionary<string, object> ExtAttrsMap { get; set; } = new();
        [JsonProperty("z_level")] public int ZLevel { get; set; }
        [JsonProperty("icon_sign")] public int IconSign { get; set; }
        [JsonProperty("point_group")] public PointGroup PointGroup { get; set; } = new();
        [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
    }
}
