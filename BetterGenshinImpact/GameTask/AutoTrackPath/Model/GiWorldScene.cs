using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoTrackPath.Model;

public class GiWorldScene
{
    /// <summary>
    /// 地图名称
    /// MapTypes
    /// </summary>
    public string MapName { get; set; } = string.Empty;

    /// <summary>
    /// 场景ID
    /// </summary>
    public int SceneId { get; set; }

    /// <summary>
    /// 场景描述
    /// </summary>
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// 场景内的坐标点集合
    /// </summary>
    public List<GiTpPosition> Points { get; set; } = new();
}