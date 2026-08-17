using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Model.Area;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class NavigationInstance
{
    private const float PositionJumpThreshold = 150;
    private readonly Dictionary<string, PositionState> _states = new();

    private sealed class PositionState
    {
        public float PrevX { get; set; } = -1;
        public float PrevY { get; set; } = -1;
        public DateTime CaptureTime { get; set; } = DateTime.MinValue;
        public bool UseTemplateMatchFallback { get; set; }
    }

    public void Reset()
    {
        _states.Clear();
    }

    public void Reset(MapLayerSelector? selector)
    {
        _states.Remove(GetStateKey(selector));
    }

    public void SetPrevPosition(float x, float y)
    {
        SetPrevPosition(x, y, MapLayerSelector.Empty);
    }

    public void SetPrevPosition(float x, float y, MapLayerSelector? selector)
    {
        var state = GetState(selector);
        (state.PrevX, state.PrevY) = (x, y);
        state.UseTemplateMatchFallback = false;
    }

    public Point2f GetPosition(ImageRegion imageRegion, string mapName, string mapMatchMethod)
    {
        return GetPosition(imageRegion, mapName, mapMatchMethod, MapLayerSelector.Empty);
    }

    public Point2f GetPosition(ImageRegion imageRegion, string mapName, string mapMatchMethod, MapLayerSelector? selector)
    {
        using var colorMat = new Mat(imageRegion.SrcMat, MapAssets.Get(imageRegion).MimiMapRect);
        var captureTime = DateTime.UtcNow;
        var state = GetState(selector);
        var primaryMap = MapManager.GetMap(mapName, mapMatchMethod, selector);
        var primaryUsesTemplateMatch = primaryMap is SceneBaseMapByTemplateMatch;
        var sceneMap = state.UseTemplateMatchFallback && !primaryUsesTemplateMatch
            ? MapManager.GetMap(mapName, "TemplateMatch", selector)
            : primaryMap;
        var p = sceneMap is SceneBaseMapByTemplateMatch templateMatchMap
            ? templateMatchMap.GetMiniMapPosition(colorMat, state.PrevX, state.PrevY, selector)
            : sceneMap.GetMiniMapPosition(colorMat, state.PrevX, state.PrevY);

        if (!state.UseTemplateMatchFallback
            && ShouldUseTemplateMatchFallback(
                primaryUsesTemplateMatch,
                p,
                new Point2f(state.PrevX, state.PrevY),
                selector))
        {
            var fallbackMap = MapManager.GetMap(mapName, "TemplateMatch", selector);
            if (fallbackMap is SceneBaseMapByTemplateMatch templateMap)
            {
                state.UseTemplateMatchFallback = true;
                p = templateMap.GetMiniMapPosition(colorMat, state.PrevX, state.PrevY, selector);
            }
        }

        UpdateStateAndNotify(state, p, captureTime);
        return p;
    }

    internal static bool ShouldUseTemplateMatchFallback(
        bool primaryUsesTemplateMatch,
        Point2f matchedPosition,
        Point2f expectedPosition,
        MapLayerSelector? selector)
    {
        if (primaryUsesTemplateMatch)
        {
            return false;
        }

        if (selector is { IsEmpty: false } || matchedPosition == default)
        {
            return true;
        }

        return expectedPosition.X > 0
               && expectedPosition.Y > 0
               && matchedPosition.DistanceTo(expectedPosition) > PositionJumpThreshold;
    }

    /// <summary>
    /// 稳定获取当前位置坐标，优先使用全地图匹配，适用于不需要高效率但需要高稳定性的场景
    /// </summary>
    /// <param name="imageRegion">图像区域</param>
    /// <param name="mapName">地图名字</param>
    /// <param name="mapMatchMethod">地图匹配方式</param>
    /// <returns>当前位置坐标</returns>
    public Point2f GetPositionStable(ImageRegion imageRegion, string mapName, string mapMatchMethod)
    {
        return GetPositionStable(imageRegion, mapName, mapMatchMethod, MapLayerSelector.Empty);
    }

    public Point2f GetPositionStable(ImageRegion imageRegion, string mapName, string mapMatchMethod, MapLayerSelector? selector)
    {
        using var colorMat = new Mat(imageRegion.SrcMat, MapAssets.Get(imageRegion).MimiMapRect);
        var captureTime = DateTime.UtcNow;
        var state = GetState(selector);

        // 先尝试使用局部匹配
        var sceneMap = MapManager.GetMap(mapName, mapMatchMethod, selector);
        // 提高局部匹配的阈值，以解决在沙漠录制点位时，移动过远不会触发全局匹配的情况
        var p = (sceneMap as SceneBaseMapByTemplateMatch)?.GetMiniMapPosition(colorMat, state.PrevX, state.PrevY, 0, selector)
                ?? sceneMap.GetMiniMapPosition(colorMat, state.PrevX, state.PrevY);

        // 如果局部匹配失败或者点位跳跃过大，再尝试全地图匹配
        if (p == default || (state.PrevX > 0 && state.PrevY > 0 && p.DistanceTo(new Point2f(state.PrevX, state.PrevY)) > 150))
        {
            Reset(selector);
            state = GetState(selector);
            sceneMap = MapManager.GetMap(mapName, mapMatchMethod, selector);
            p = sceneMap is SceneBaseMapByTemplateMatch templateMatchMap
                ? templateMatchMap.GetMiniMapPosition(colorMat, state.PrevX, state.PrevY, selector)
                : sceneMap.GetMiniMapPosition(colorMat, state.PrevX, state.PrevY);
        }

        UpdateStateAndNotify(state, p, captureTime);
        return p;
    }

    public Point2f GetPositionStableByCache(ImageRegion imageRegion, string mapName, string mapMatchingMethod, int cacheTimeMs = 900)
    {
        return GetPositionStableByCache(imageRegion, mapName, mapMatchingMethod, MapLayerSelector.Empty, cacheTimeMs);
    }

    public Point2f GetPositionStableByCache(ImageRegion imageRegion, string mapName, string mapMatchingMethod, MapLayerSelector? selector, int cacheTimeMs = 900)
    {
        var cached = GetCachedPosition(selector, cacheTimeMs);
        if (cached is { } p)
        {
            return p;
        }

        return GetPositionStable(imageRegion, mapName, mapMatchingMethod, selector);
    }
    public Point2f? GetCachedPosition(MapLayerSelector? selector, int cacheTimeMs = 900)
    {
        if (!_states.TryGetValue(GetStateKey(selector), out var state))
        {
            return null;
        }

        var captureTime = DateTime.UtcNow;
        if (captureTime - state.CaptureTime < TimeSpan.FromMilliseconds(cacheTimeMs) && state.PrevX > 0 && state.PrevY > 0)
        {
            return new Point2f(state.PrevX, state.PrevY);
        }

        return null;
    }

    private void UpdateStateAndNotify(PositionState state, Point2f p, DateTime captureTime)
    {
        if (p != default && captureTime > state.CaptureTime)
        {
            (state.PrevX, state.PrevY) = (p.X, p.Y);
            state.CaptureTime = captureTime;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(typeof(Navigation),
            "SendCurrentPosition", new object(), p));
    }

    private PositionState GetState(MapLayerSelector? selector)
    {
        var key = GetStateKey(selector);
        if (!_states.TryGetValue(key, out var state))
        {
            state = new PositionState();
            _states[key] = state;
        }

        return state;
    }

    private static string GetStateKey(MapLayerSelector? selector)
    {
        return MapManager.GetSelectorStateKey(selector);
    }
}
