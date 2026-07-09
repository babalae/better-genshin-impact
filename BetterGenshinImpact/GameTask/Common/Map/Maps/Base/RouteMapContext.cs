using System;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>
/// Map identity that must travel together through route execution.
/// </summary>
[Serializable]
public sealed class RouteMapContext
{
    public RouteMapContext(string mapName, string? mapMatchMethod, MapLayerSelector? layerSelector)
    {
        MapName = string.IsNullOrWhiteSpace(mapName) ? nameof(MapTypes.Teyvat) : mapName;
        MapMatchMethod = mapMatchMethod ?? string.Empty;
        LayerSelector = layerSelector ?? MapLayerSelector.Empty;
    }

    public string MapName { get; }

    public string MapMatchMethod { get; }

    public MapLayerSelector LayerSelector { get; }

    public static RouteMapContext Legacy(string mapName, string? mapMatchMethod)
    {
        return new RouteMapContext(mapName, mapMatchMethod, MapLayerSelector.Empty);
    }

    public RouteMapContext WithSelector(MapLayerSelector? selector)
    {
        return new RouteMapContext(MapName, MapMatchMethod, selector);
    }

    public RouteMapContext WithMapName(string mapName)
    {
        return new RouteMapContext(mapName, MapMatchMethod, LayerSelector);
    }

    public override string ToString()
    {
        return $"{MapName}|{MapMatchMethod}|{LayerSelector.StateKey}";
    }
}
