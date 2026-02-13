using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Model.MihoyoMap.Responses
{
    public class LabelNode
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("icon")] public string Icon { get; set; } = string.Empty;
        [JsonProperty("parent_id")] public int ParentId { get; set; }
        [JsonProperty("depth")] public int Depth { get; set; }
        [JsonProperty("node_type")] public int NodeType { get; set; }
        [JsonProperty("jump_type")] public int JumpType { get; set; }
        [JsonProperty("jump_target_id")] public int JumpTargetId { get; set; }
        [JsonProperty("wiki_jump_url")] public string WikiJumpUrl { get; set; } = string.Empty;
        [JsonProperty("display_priority")] public int DisplayPriority { get; set; }
        [JsonProperty("children")] public List<LabelNode> Children { get; set; } = new();
        [JsonProperty("activity_page_label")] public int ActivityPageLabel { get; set; }
        [JsonProperty("area_page_label")] public List<int> AreaPageLabel { get; set; } = new();
        [JsonProperty("is_all_area")] public bool IsAllArea { get; set; }
        [JsonProperty("default_show")] public bool DefaultShow { get; set; }
        [JsonProperty("ch_ext")] public string ChExt { get; set; } = string.Empty;
        [JsonProperty("sort")] public int Sort { get; set; }
        [JsonProperty("area_label_list")] public List<int> AreaLabelList { get; set; } = new();
        [JsonProperty("ext_attr_list")] public List<object> ExtAttrList { get; set; } = new();
        [JsonProperty("recommend_route_list")] public List<object> RecommendRouteList { get; set; } = new();
        [JsonProperty("point_count")] public int PointCount { get; set; }
        [JsonProperty("alias_name")] public string AliasName { get; set; } = string.Empty;
        [JsonProperty("item_id")] public int ItemId { get; set; }
        [JsonProperty("rec_refresh_hour")] public int RecRefreshHour { get; set; }
        [JsonProperty("tips")] public string Tips { get; set; } = string.Empty;
        [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
    }
}
