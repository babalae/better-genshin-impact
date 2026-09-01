using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 特殊相邻传送点清单条目（用户可配 JSON，运行时可增改）。
/// 命中判定：与本次传送最近真实传送点 nTpPoints[0] 的欧氏距离 &lt;= 容差（1024 区块坐标系）。
/// 详见 .kiro/specs/teleport-adjacent-point-misclick-zoom-whitelist-fix/design.md。
/// </summary>
public class SpecialAdjacentTpPoint
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    /// <summary>可选专属缩放；null 时用全局默认 2.5。数值越小地图放得越大。</summary>
    [JsonPropertyName("zoom")]
    public double? Zoom { get; set; }

    /// <summary>可选备注，仅供人读，不参与判定。</summary>
    [JsonPropertyName("remark")]
    public string? Remark { get; set; }
}
