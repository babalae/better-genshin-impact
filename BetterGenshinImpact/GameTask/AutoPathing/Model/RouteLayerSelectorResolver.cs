using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.GameTask.AutoPathing.Model;

public static class RouteLayerSelectorResolver
{
    public static IReadOnlyList<string> ValidateTask(PathingTask task)
    {
        var diagnostics = new List<string>();
        var segments = task.Info.MapLayerSegments ?? [];

        ValidateSelectorMode("info", task.Info.LayerSelector, diagnostics);
        foreach (var waypoint in task.Positions)
        {
            ValidateSelectorMode($"waypoint id {waypoint.Id?.ToString() ?? "<missing>"}", waypoint.LayerSelector, diagnostics);
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            ValidateSelectorMode($"segment[{i}]", segment.Selector, diagnostics);

            if (!segment.HasSelectorFields)
            {
                diagnostics.Add($"segment[{i}] has no map_layer selector fields.");
                continue;
            }

            if (!segment.FromId.HasValue || !segment.ToId.HasValue)
            {
                diagnostics.Add($"segment[{i}] must declare both from_id and to_id.");
                continue;
            }

            if (segment.FromId.Value > segment.ToId.Value)
            {
                diagnostics.Add($"segment[{i}] has inverted id range {segment.FromId.Value}>{segment.ToId.Value}.");
            }
        }

        for (var i = 0; i < segments.Count; i++)
        {
            var left = segments[i];
            if (!IsValidComparableSegment(left))
            {
                continue;
            }

            for (var j = i + 1; j < segments.Count; j++)
            {
                var right = segments[j];
                if (!IsValidComparableSegment(right))
                {
                    continue;
                }

                var overlaps = left.FromId!.Value <= right.ToId!.Value && right.FromId!.Value <= left.ToId!.Value;
                if (overlaps && !left.Selector.Equals(right.Selector))
                {
                    diagnostics.Add($"segment[{i}] and segment[{j}] overlap with incompatible selectors.");
                }
            }
        }

        ValidateKnownRouteLevelSafety(task, diagnostics);
        return diagnostics;
    }

    public static MapLayerSelector ResolveEffectiveSelector(PathingTask task, Waypoint waypoint)
    {
        return ResolveEffectiveSelector(task.Info, waypoint, out _);
    }

    public static MapLayerSelector ResolveEffectiveSelector(
        PathingTaskInfo info,
        Waypoint waypoint,
        out IReadOnlyList<string> diagnostics)
    {
        var messages = new List<string>();

        if (waypoint.LayerSelector.HasAnyField)
        {
            diagnostics = messages;
            return waypoint.LayerSelector.IsEmpty ? MapLayerSelector.Empty : waypoint.LayerSelector;
        }

        var segments = info.MapLayerSegments ?? [];
        if (segments.Count > 0 && waypoint.Id is null)
        {
            messages.Add("Waypoint has no id; map_layer_segments cannot match by ordinal.");
        }

        var matchedSegments = waypoint.Id is null
            ? []
            : segments.Where(segment => segment.HasSelectorFields && segment.ContainsWaypointId(waypoint.Id)).ToList();

        if (matchedSegments.Count > 0)
        {
            var selector = matchedSegments[0].Selector;
            diagnostics = messages;
            return selector.IsEmpty ? MapLayerSelector.Empty : selector;
        }

        if (info.LayerSelector.HasAnyField)
        {
            diagnostics = messages;
            return info.LayerSelector.IsEmpty ? MapLayerSelector.Empty : info.LayerSelector;
        }

        diagnostics = messages;
        return MapLayerSelector.Empty;
    }

    public static bool HasWaypointLayerOverride(PathingTask task)
    {
        return task.Positions.Any(waypoint => waypoint.LayerSelector.HasAnyField);
    }

    private static bool IsValidComparableSegment(MapLayerSegment segment)
    {
        return segment.HasSelectorFields
               && segment.FromId.HasValue
               && segment.ToId.HasValue
               && segment.FromId.Value <= segment.ToId.Value;
    }

    private static void ValidateSelectorMode(string scope, MapLayerSelector selector, List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(selector.MapLayerMode))
        {
            return;
        }

        var mode = selector.MapLayerMode.Trim().ToLowerInvariant();
        if (mode is not (MapLayerSelector.ModeUnspecified or MapLayerSelector.ModePrefer or MapLayerSelector.ModeRequire))
        {
            diagnostics.Add($"{scope} has invalid map_layer_mode '{selector.MapLayerMode}'.");
        }
    }

    private static void ValidateKnownRouteLevelSafety(PathingTask task, List<string> diagnostics)
    {
        if (!IsRoute523(task.Info.Name))
        {
            return;
        }

        var routeSelector = task.Info.LayerSelector;
        if (routeSelector.IsEmpty)
        {
            return;
        }

        if (string.Equals(routeSelector.MapLayerId, "3340101", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add("Route 523 must not use broad route-level layer 3340101.");
        }

        var hasSegmentOrWaypointOverrides = (task.Info.MapLayerSegments?.Any(segment => segment.HasSelectorFields) ?? false)
                                            || HasWaypointLayerOverride(task);
        if (!hasSegmentOrWaypointOverrides)
        {
            diagnostics.Add("Route 523 must not use route-level-only map layer metadata.");
        }
    }

    private static bool IsRoute523(string? routeName)
    {
        return !string.IsNullOrWhiteSpace(routeName)
               && routeName.TrimStart().StartsWith("523", StringComparison.Ordinal);
    }
}
