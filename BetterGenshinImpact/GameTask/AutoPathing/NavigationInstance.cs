using System;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Model.Area;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing;

public class NavigationInstance
{
    /// <summary>
    /// 保护最近匹配状态和截图时间，避免并发截图以错误顺序覆盖状态。
    /// </summary>
    private readonly object _previousMatchLock = new();

    /// <summary>
    /// 当前导航实例最近一次成功的小地图匹配结果，包含坐标和分组分层来源。
    /// 状态保存在实例上，避免共享的 SceneBaseMap 在多实例间串用楼层。
    /// </summary>
    private MiniMapMatchState? _previousMatch;

    /// <summary>
    /// 最近一次成功结果保留下来的候选排序依据。
    /// 即使主动清除上次点位，也继续用它排列已经加载的分组分层地图，但不会用它执行局部范围筛选。
    /// </summary>
    private MiniMapMatchState? _lastMatchForOrdering;

    /// <summary>
    /// 最近一次写入匹配状态的截图时间；较旧截图的结果不能覆盖较新结果。
    /// </summary>
    private DateTime _captureTime = DateTime.MinValue;

    /// <summary>
    /// 清除用于局部匹配的上次点位；最近一次楼层排序依据会继续保留。
    /// </summary>
    public void Reset()
    {
        lock (_previousMatchLock)
        {
            _previousMatch = null;
        }
    }
    
    /// <summary>
    /// 设置外部提供的初始参考底图坐标。外部坐标没有可用的楼层来源，因此从 floor 0 开始排序。
    /// </summary>
    public void SetPrevPosition(float x, float y)
    {
        lock (_previousMatchLock)
        {
            var previousMatch = new MiniMapMatchState(
                new Point2f(x, y),
                0,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                false);
            _previousMatch = previousMatch;
            _lastMatchForOrdering = previousMatch;
        }
    }

    public Point2f GetPosition(ImageRegion imageRegion, string mapName, string mapMatchMethod)
    {
        using var colorMat = new Mat(imageRegion.SrcMat, MapAssets.Get(imageRegion).MimiMapRect);
        var captureTime = DateTime.UtcNow;
        var sceneMap = MapManager.GetMap(mapName, mapMatchMethod);
        var previousMatch = GetPreviousMatch(mapName);
        var orderingReference = GetLayerOrderingReference(mapName);
        var matchResult = MatchMiniMap(sceneMap, colorMat, previousMatch, orderingReference, mapName, 2);
        var p = matchResult?.Position ?? default;
        UpdatePreviousMatch(matchResult, captureTime, sceneMap is SceneBaseMap and not SceneBaseMapByTemplateMatch);
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(typeof(Navigation),
            "SendCurrentPosition", new object(), p));
        return p;
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
        return GetPositionStable(imageRegion, mapName, mapMatchMethod, out _);
    }

    /// <summary>
    /// 稳定获取当前位置坐标，并返回本次最终命中的地图层是否来自分组分层地图。
    /// 分层来源信息供需要同步游戏地图缩放状态的调用方使用，不改变返回坐标所属的参考底图坐标系。
    /// </summary>
    /// <param name="imageRegion">图像区域。</param>
    /// <param name="mapName">地图名字。</param>
    /// <param name="mapMatchMethod">地图匹配方式。</param>
    /// <param name="isGroupLayer">本次最终成功结果是否来自分组分层地图。</param>
    /// <returns>当前位置坐标。</returns>
    internal Point2f GetPositionStable(
        ImageRegion imageRegion,
        string mapName,
        string mapMatchMethod,
        out bool isGroupLayer)
    {
        using var colorMat = new Mat(imageRegion.SrcMat, MapAssets.Get(imageRegion).MimiMapRect);
        var captureTime = DateTime.UtcNow;

        // 先尝试使用局部匹配
        var sceneMap = MapManager.GetMap(mapName, mapMatchMethod);
        var previousMatch = GetPreviousMatch(mapName);
        var orderingReference = GetLayerOrderingReference(mapName);
        //提高局部匹配的阈值，以解决在沙漠录制点位时，移动过远不会触发全局匹配的情况
        var matchResult = MatchMiniMap(sceneMap, colorMat, previousMatch, orderingReference, mapName, 0);
        var p = matchResult?.Position ?? default;

        // 如果局部匹配失败或者点位跳跃过大，再尝试全地图匹配
        if (p == default || previousMatch is { Position.X: > 0, Position.Y: > 0 } && p.DistanceTo(previousMatch.Position) > 150)
        {
            ResetForFallback(captureTime);
            matchResult = MatchMiniMap(sceneMap, colorMat, null, orderingReference, mapName, 2);
            p = matchResult?.Position ?? default;
        }
        isGroupLayer = matchResult?.IsGroupLayer == true;
        UpdatePreviousMatch(matchResult, captureTime, sceneMap is SceneBaseMap and not SceneBaseMapByTemplateMatch);

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(typeof(Navigation),
            "SendCurrentPosition", new object(), p));
        return p;
    }

    public Point2f GetPositionStableByCache(ImageRegion imageRegion, string mapName, string mapMatchingMethod, int cacheTimeMs = 900)
    {
        var captureTime = DateTime.UtcNow;
        lock (_previousMatchLock)
        {
            if (_previousMatch is { Position.X: > 0, Position.Y: > 0 } previousMatch &&
                (string.IsNullOrEmpty(previousMatch.MapName) || previousMatch.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase)) &&
                captureTime - _captureTime < TimeSpan.FromMilliseconds(cacheTimeMs))
            {
                return previousMatch.Position;
            }
        }

        return GetPositionStable(imageRegion, mapName, mapMatchingMethod);
    }

    /// <summary>
    /// 获取可供指定地图继续局部匹配的上次状态，跨地图状态不会被复用。
    /// </summary>
    private MiniMapMatchState? GetPreviousMatch(string mapName)
    {
        lock (_previousMatchLock)
        {
            if (_previousMatch == null ||
                (!string.IsNullOrEmpty(_previousMatch.MapName) &&
                 !_previousMatch.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
            return _previousMatch;
        }
    }

    /// <summary>
    /// 获取指定地图最近一次成功匹配留下的排序依据，跨地图的排序状态不会被复用。
    /// </summary>
    private MiniMapMatchState? GetLayerOrderingReference(string mapName)
    {
        lock (_previousMatchLock)
        {
            if (_lastMatchForOrdering == null ||
                (!string.IsNullOrEmpty(_lastMatchForOrdering.MapName) &&
                 !_lastMatchForOrdering.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return _lastMatchForOrdering;
        }
    }

    /// <summary>
    /// 为当前截图的全局回退清除旧状态。若已有更新截图写入结果，则不允许较旧截图清除它。
    /// </summary>
    private void ResetForFallback(DateTime captureTime)
    {
        lock (_previousMatchLock)
        {
            if (captureTime > _captureTime)
            {
                _previousMatch = null;
            }
        }
    }

    /// <summary>
    /// 按地图实现执行小地图匹配，并把公共坐标结果补充为统一的楼层匹配状态。
    /// </summary>
    private static MiniMapMatchState? MatchMiniMap(
        ISceneMap sceneMap,
        Mat colorMiniMapMat,
        MiniMapMatchState? previousMatch,
        MiniMapMatchState? orderingReference,
        string mapName,
        int templateMatchRank)
    {
        if (sceneMap is SceneBaseMapByTemplateMatch templateMap)
        {
            var p = previousMatch == null
                ? templateMap.GetMiniMapPosition(colorMiniMapMat)
                : templateMap.GetMiniMapPosition(
                    colorMiniMapMat,
                    previousMatch.Position.X,
                    previousMatch.Position.Y,
                    templateMatchRank);
            if (p == default)
            {
                return null;
            }

            var layer = templateMap.PrevSuccessResult.Layer;
            return new MiniMapMatchState(
                p,
                layer?.Floor ?? 0,
                layer?.LayerId ?? string.Empty,
                layer?.LayerGroupId ?? string.Empty,
                layer?.Name ?? string.Empty,
                mapName,
                false);
        }

        if (sceneMap is SceneBaseMap sceneBaseMap)
        {
            return sceneBaseMap.GetMiniMapMatchResult(colorMiniMapMat, previousMatch, orderingReference);
        }

        var point = previousMatch == null
            ? sceneMap.GetMiniMapPosition(colorMiniMapMat)
            : sceneMap.GetMiniMapPosition(colorMiniMapMat, previousMatch.Position.X, previousMatch.Position.Y);
        return point == default
            ? null
            : new MiniMapMatchState(point, 0, string.Empty, string.Empty, string.Empty, mapName, false);
    }

    /// <summary>
    /// 仅使用更新截图得到的成功结果推进状态，并在楼层发生变化时记录一次日志。
    /// </summary>
    private void UpdatePreviousMatch(MiniMapMatchState? matchResult, DateTime captureTime, bool enableFloorSwitchLog)
    {
        if (matchResult == null)
        {
            return;
        }

        lock (_previousMatchLock)
        {
            if (captureTime <= _captureTime)
            {
                return;
            }
            if (enableFloorSwitchLog &&
                _previousMatch != null &&
                (string.IsNullOrEmpty(_previousMatch.MapName) ||
                 _previousMatch.MapName.Equals(matchResult.MapName, StringComparison.OrdinalIgnoreCase)) &&
                _previousMatch.Floor != matchResult.Floor)
            {
                TaskControl.Logger.LogInformation("[SIFT] 已切换至 {Name}（group={GroupId}, floor={Floor}）",
                    matchResult.LayerName, matchResult.LayerGroupId, matchResult.Floor);
            }

            _previousMatch = matchResult;
            _lastMatchForOrdering = matchResult;
            _captureTime = captureTime;
        }
    }
}
