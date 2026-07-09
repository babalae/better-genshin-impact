using System;
using System.Text.Json.Serialization;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

[Serializable]
public class MapLayerSegment
{
    public int? FromId { get; set; }

    public int? ToId { get; set; }

    public string? MapLayerId { get; set; }

    public string? MapLayerGroupId { get; set; }

    public int? MapLayerFloor { get; set; }

    public string? MapLayerMode { get; set; }

    [JsonIgnore]
    public bool HasSelectorFields => MapLayerSelector.HasAnyFieldValues(MapLayerId, MapLayerGroupId, MapLayerFloor, MapLayerMode);

    [JsonIgnore]
    public MapLayerSelector Selector => MapLayerSelector.FromFields(MapLayerId, MapLayerGroupId, MapLayerFloor, MapLayerMode);

    public bool ContainsWaypointId(int? waypointId)
    {
        return waypointId.HasValue
               && FromId.HasValue
               && ToId.HasValue
               && FromId.Value <= waypointId.Value
               && waypointId.Value <= ToId.Value;
    }
}
