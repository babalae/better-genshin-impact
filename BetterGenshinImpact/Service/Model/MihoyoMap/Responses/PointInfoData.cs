using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Model.MihoyoMap.Responses
{
    public class PointInfoData
    {
        [JsonProperty("info")] public PointInfo Info { get; set; } = new PointInfo();
        [JsonProperty("correct_user_list")] public List<PointCorrectUser> CorrectUserList { get; set; } = new List<PointCorrectUser>();
        [JsonProperty("last_update_time")] public string LastUpdateTime { get; set; } = string.Empty;
        [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
    }

    public class PointInfo
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("map_id")] public int MapId { get; set; }
        [JsonProperty("label_id")] public int LabelId { get; set; }
        [JsonProperty("x_pos")] public double XPos { get; set; }
        [JsonProperty("y_pos")] public double YPos { get; set; }
        [JsonProperty("expansion")] public string Expansion { get; set; } = string.Empty;
        [JsonProperty("display_state")] public int DisplayState { get; set; }
        [JsonProperty("version")] public int Version { get; set; }
        [JsonProperty("editor")] public string Editor { get; set; } = string.Empty;
        [JsonProperty("editor_name")] public string EditorName { get; set; } = string.Empty;
        [JsonProperty("author")] public string Author { get; set; } = string.Empty;
        [JsonProperty("author_name")] public string AuthorName { get; set; } = string.Empty;
        [JsonProperty("ctime")] public string Ctime { get; set; } = string.Empty;
        [JsonProperty("content")] public string Content { get; set; } = string.Empty;
        [JsonProperty("img")] public string Img { get; set; } = string.Empty;
        [JsonProperty("url_list")] public List<PointInfoUrlItem> UrlList { get; set; } = new List<PointInfoUrlItem>();
        [JsonProperty("record_id")] public string RecordId { get; set; } = string.Empty;
        [JsonProperty("area_id")] public int AreaId { get; set; }
        [JsonProperty("ext_attrs")] public string ExtAttrs { get; set; } = string.Empty;
        [JsonProperty("z_level")] public int ZLevel { get; set; }
        [JsonProperty("icon_sign")] public int IconSign { get; set; }
        [JsonProperty("video")] public PointInfoVideo Video { get; set; } = new PointInfoVideo();
        [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
    }

    public class PointInfoUrlItem
    {
        [JsonProperty("text")] public string Text { get; set; } = string.Empty;
        [JsonProperty("url")] public string Url { get; set; } = string.Empty;
    }

    public class PointInfoVideo
    {
        [JsonProperty("cover_url")] public string CoverUrl { get; set; } = string.Empty;
        [JsonProperty("duration")] public int Duration { get; set; }
        [JsonProperty("detail")] public JToken? Detail { get; set; }
        [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
    }

    public class PointCorrectUser
    {
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("img")] public string Img { get; set; } = string.Empty;
        [JsonProperty("ctime")] public string Ctime { get; set; } = string.Empty;
        [JsonExtensionData] public IDictionary<string, JToken>? Extra { get; set; }
    }
}
