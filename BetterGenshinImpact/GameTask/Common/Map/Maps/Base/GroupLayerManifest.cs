using System.Collections.Generic;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>
/// 分组分层地图清单。
/// 当前数据来源是 <c>layers_sift.json</c>，类型名称不与具体匹配算法绑定，便于后续复用到其他匹配方式。
/// </summary>
internal sealed class GroupLayerManifest
{
    /// <summary>
    /// 清单所属的地图类型。
    /// </summary>
    [JsonProperty("mapType")]
    public string MapType { get; set; } = string.Empty;

    /// <summary>
    /// 清单使用的参考底图宽度。
    /// </summary>
    [JsonProperty("referenceWidth")]
    public int ReferenceWidth { get; set; }

    /// <summary>
    /// 清单使用的参考底图高度。
    /// </summary>
    [JsonProperty("referenceHeight")]
    public int ReferenceHeight { get; set; }

    /// <summary>
    /// 清单内的所有分组分层片段。
    /// </summary>
    [JsonProperty("fragments")]
    public List<GroupLayerFragmentManifest> Fragments { get; set; } = [];
}

/// <summary>
/// 单个分组分层片段的清单数据。
/// </summary>
internal sealed class GroupLayerFragmentManifest
{
    /// <summary>
    /// 片段的稳定唯一标识，也是当前资源文件的名称前缀。
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 片段所属的分层区域标识。
    /// </summary>
    [JsonProperty("groupId")]
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// 用于日志和诊断的可读名称。
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 片段对应的楼层编号。
    /// </summary>
    [JsonProperty("floor")]
    public int Floor { get; set; }

    /// <summary>
    /// 片段裁剪范围的左上角 X 坐标。
    /// </summary>
    [JsonProperty("cropX")]
    public int CropX { get; set; }

    /// <summary>
    /// 片段裁剪范围的左上角 Y 坐标。
    /// </summary>
    [JsonProperty("cropY")]
    public int CropY { get; set; }

    /// <summary>
    /// 片段裁剪范围的宽度。
    /// </summary>
    [JsonProperty("cropWidth")]
    public int CropWidth { get; set; }

    /// <summary>
    /// 片段裁剪范围的高度。
    /// </summary>
    [JsonProperty("cropHeight")]
    public int CropHeight { get; set; }

    /// <summary>
    /// 当前楼层实际高亮范围的左上角 X 坐标。
    /// </summary>
    [JsonProperty("highlightX")]
    public int HighlightX { get; set; }

    /// <summary>
    /// 当前楼层实际高亮范围的左上角 Y 坐标。
    /// </summary>
    [JsonProperty("highlightY")]
    public int HighlightY { get; set; }

    /// <summary>
    /// 当前楼层实际高亮范围的宽度。
    /// </summary>
    [JsonProperty("highlightWidth")]
    public int HighlightWidth { get; set; }

    /// <summary>
    /// 当前楼层实际高亮范围的高度。
    /// </summary>
    [JsonProperty("highlightHeight")]
    public int HighlightHeight { get; set; }

    /// <summary>
    /// 当前特征资源中记录的特征点数量。
    /// </summary>
    [JsonProperty("featureCount")]
    public int FeatureCount { get; set; }

    /// <summary>
    /// 关键点文件相对于清单目录的路径。
    /// </summary>
    [JsonProperty("keyPointFile")]
    public string KeyPointFile { get; set; } = string.Empty;

    /// <summary>
    /// 特征描述文件相对于清单目录的路径。
    /// </summary>
    [JsonProperty("descriptorFile")]
    public string DescriptorFile { get; set; } = string.Empty;
}
