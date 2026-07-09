using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>
/// Optional route layer selector for layered template-match maps.
/// Empty/unspecified selectors intentionally preserve legacy all-layer behavior.
/// </summary>
[Serializable]
public sealed class MapLayerSelector : IEquatable<MapLayerSelector>
{
    public const string ModeUnspecified = "unspecified";
    public const string ModePrefer = "prefer";
    public const string ModeRequire = "require";

    public static MapLayerSelector Empty { get; } = new();

    public string? MapLayerId { get; init; }

    public string? MapLayerGroupId { get; init; }

    public int? MapLayerFloor { get; init; }

    public string? MapLayerMode { get; init; }

    [JsonIgnore]
    public bool HasAnyField => HasAnyFieldValues(MapLayerId, MapLayerGroupId, MapLayerFloor, MapLayerMode);

    [JsonIgnore]
    public bool HasCriteria =>
        !string.IsNullOrWhiteSpace(MapLayerId)
        || !string.IsNullOrWhiteSpace(MapLayerGroupId)
        || MapLayerFloor.HasValue;

    [JsonIgnore]
    public string NormalizedMode
    {
        get
        {
            if (string.IsNullOrWhiteSpace(MapLayerMode))
            {
                // A route that declares an id/group/floor without a mode is still an explicit selector.
                return HasCriteria ? ModeRequire : ModeUnspecified;
            }

            var mode = MapLayerMode.Trim().ToLowerInvariant();
            return mode switch
            {
                ModeRequire => ModeRequire,
                ModePrefer => ModePrefer,
                ModeUnspecified => ModeUnspecified,
                _ => ModeUnspecified
            };
        }
    }

    [JsonIgnore]
    public bool IsEmpty => !HasCriteria || NormalizedMode == ModeUnspecified;

    [JsonIgnore]
    public bool IsRequire => !IsEmpty && NormalizedMode == ModeRequire;

    [JsonIgnore]
    public bool IsPrefer => !IsEmpty && NormalizedMode == ModePrefer;

    [JsonIgnore]
    public string StateKey => IsEmpty
        ? "legacy"
        : $"{NormalizedMode}|id={NormalizePart(MapLayerId)}|group={NormalizePart(MapLayerGroupId)}|floor={MapLayerFloor?.ToString() ?? "-"}";

    public static bool HasAnyFieldValues(string? layerId, string? layerGroupId, int? floor, string? mode)
    {
        return !string.IsNullOrWhiteSpace(layerId)
               || !string.IsNullOrWhiteSpace(layerGroupId)
               || floor.HasValue
               || !string.IsNullOrWhiteSpace(mode);
    }

    public static MapLayerSelector FromFields(string? layerId, string? layerGroupId, int? floor, string? mode)
    {
        if (!HasAnyFieldValues(layerId, layerGroupId, floor, mode))
        {
            return Empty;
        }

        return new MapLayerSelector
        {
            MapLayerId = NullIfWhiteSpace(layerId),
            MapLayerGroupId = NullIfWhiteSpace(layerGroupId),
            MapLayerFloor = floor,
            MapLayerMode = NullIfWhiteSpace(mode)
        };
    }

    public bool Matches(BaseMapLayerByTemplateMatch layer)
    {
        if (IsEmpty)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(MapLayerId)
            && !string.Equals(layer.LayerId, MapLayerId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(MapLayerGroupId)
            && !string.Equals(layer.LayerGroupId, MapLayerGroupId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (MapLayerFloor.HasValue && layer.Floor != MapLayerFloor.Value)
        {
            return false;
        }

        return true;
    }

    public static IReadOnlyList<BaseMapLayerByTemplateMatch> FilterAndOrderLayers(
        IEnumerable<BaseMapLayerByTemplateMatch> layers,
        MapLayerSelector? selector)
    {
        var layerList = layers.ToList();
        var normalizedSelector = selector ?? Empty;
        if (normalizedSelector.IsEmpty)
        {
            return layerList;
        }

        return layerList
            .Where(normalizedSelector.Matches)
            .OrderBy(layer => GetMatchRank(layer, normalizedSelector))
            .ThenBy(layer => layer.Floor)
            .ThenBy(layer => layer.LayerGroupId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(layer => layer.LayerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool Equals(MapLayerSelector? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(StateKey, other.StateKey, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is MapLayerSelector other && Equals(other);
    }

    public override int GetHashCode()
    {
        return StringComparer.Ordinal.GetHashCode(StateKey);
    }

    public override string ToString()
    {
        return StateKey;
    }

    private static int GetMatchRank(BaseMapLayerByTemplateMatch layer, MapLayerSelector selector)
    {
        if (!string.IsNullOrWhiteSpace(selector.MapLayerId)
            && string.Equals(layer.LayerId, selector.MapLayerId, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(selector.MapLayerGroupId)
            && string.Equals(layer.LayerGroupId, selector.MapLayerGroupId, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (selector.MapLayerFloor.HasValue && layer.Floor == selector.MapLayerFloor.Value)
        {
            return 2;
        }

        return 3;
    }

    private static string NormalizePart(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
