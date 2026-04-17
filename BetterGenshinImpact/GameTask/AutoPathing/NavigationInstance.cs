using System;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.Model.Area;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// Execution state instance maintaining CV matching history and telemetry context.
/// 维持视觉匹配历史记录及遥测上下文记录的执行状态实例。
/// </summary>
public class NavigationInstance
{
    private float _prevX = -1f;
    private float _prevY = -1f;
    private DateTime _captureTime = DateTime.MinValue;

    /// <summary>
    /// Clears internal kinematic tracking caches to force coordinate recalculation.
    /// 清除内部的运动学追踪缓存以强制重新计算坐标。
    /// </summary>
    public void Reset()
    {
        _prevX = -1f;
        _prevY = -1f;
    }
    
    /// <summary>
    /// Bootstraps prior tracking memory without CV map matching execution.
    /// 直接引导先前的追踪存储器，绕过视觉地图匹配执行过程。
    /// </summary>
    /// <param name="x">Known valid map X. 已知有效的地图X坐标。</param>
    /// <param name="y">Known valid map Y. 已知有效的地图Y坐标。</param>
    public void SetPrevPosition(float x, float y)
    {
        if (float.IsNaN(x) || float.IsNaN(y)) return;
        _prevX = x;
        _prevY = y;
    }

    /// <summary>
    /// Performs standard localized coordinate resolution through partial map template matching.
    /// 通过局部地图模板匹配执行标准的位置坐标解析。
    /// </summary>
    /// <param name="imageRegion">The viewport rendering buffer region. 视口渲染缓冲区域。</param>
    /// <param name="mapName">Name of logical region bound to the current context. 当前上下文绑定的逻辑区域名称。</param>
    /// <param name="mapMatchMethod">Specifies the internal algorithmic logic implementation. 指定内部算法逻辑实现。</param>
    /// <returns>Computed current coordinate context. 计算出的当前坐标上下文。</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="imageRegion"/> is null. 当 <paramref name="imageRegion"/> 为 null 时抛出。</exception>
    public Point2f GetPosition(ImageRegion imageRegion, string mapName, string mapMatchMethod)
    {
        ArgumentNullException.ThrowIfNull(imageRegion);
        
        if (imageRegion.SrcMat == null || imageRegion.SrcMat.IsDisposed)
            return default;

        using var colorMat = new Mat(imageRegion.SrcMat, MapAssets.Instance.MimiMapRect);
        var captureTime = DateTime.UtcNow;

        var mapBase = MapManager.GetMap(mapName, mapMatchMethod);
        var p = mapBase.GetMiniMapPosition(colorMat, _prevX, _prevY);

        if (p != default && captureTime > _captureTime)
        {
            _prevX = p.X;
            _prevY = p.Y;
            _captureTime = captureTime;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "SendCurrentPosition", new object(), p));

        return p;
    }

    /// <summary>
    /// Acquires coordinates robustly utilizing slow global matching if local tracking is destabilized.
    /// 稳定获取当前位置坐标，优先使用全地图匹配，适用于不需要高效率但需要高稳定性的场景（解决跳变）。
    /// </summary>
    /// <param name="imageRegion">The root visual tracking sector matrix. 根部视觉追踪扇区矩阵。</param>
    /// <param name="mapName">Teyvat region topological descriptor. 提瓦特区域拓扑描述符。</param>
    /// <param name="mapMatchMethod">Mapping resolution configuration type. 映射解析配置类型。</param>
    /// <returns>Resilient absolute position coordinates. 稳健的绝对位置坐标。</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="imageRegion"/> is null. 当 <paramref name="imageRegion"/> 为 null 时抛出。</exception>
    public Point2f GetPositionStable(ImageRegion imageRegion, string mapName, string mapMatchMethod)
    {
        ArgumentNullException.ThrowIfNull(imageRegion);
        
        if (imageRegion.SrcMat == null || imageRegion.SrcMat.IsDisposed)
            return default;

        using var colorMat = new Mat(imageRegion.SrcMat, MapAssets.Instance.MimiMapRect);
        var captureTime = DateTime.UtcNow;

        // Perform local search phase first 
        // 先尝试使用局部匹配
        var sceneMap = MapManager.GetMap(mapName, mapMatchMethod);

        // Elevate local threshold resolving spatial teleport discontinuities in desolation zones
        // 提高局部匹配的阈值，以解决在沙漠录制点位时移动过远不会触发全局匹配的情况
        var p = (sceneMap as SceneBaseMapByTemplateMatch)?.GetMiniMapPosition(colorMat, _prevX, _prevY, 0)
                ?? sceneMap.GetMiniMapPosition(colorMat, _prevX, _prevY);

        // Trigger heavy global search loop if local matching degenerates
        // 如果局部匹配失败或者点位跳跃过大，再尝试全地图匹配
        bool isInvalidLocalp = p == default;
        bool isDistanceTooFar = _prevX > 0f && _prevY > 0f && p.DistanceTo(new Point2f(_prevX, _prevY)) > 150.0;

        if (isInvalidLocalp || isDistanceTooFar)
        {
            Reset();
            // Full map scanning procedure 
            p = sceneMap.GetMiniMapPosition(colorMat, _prevX, _prevY);
        }

        if (p != default && captureTime > _captureTime)
        {
            _prevX = p.X;
            _prevY = p.Y;
            _captureTime = captureTime;
        }

        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this,
            "SendCurrentPosition", new object(), p));

        return p;
    }

    /// <summary>
    /// Acquires verified coordinates bounded by an expiration cache timeout to decouple thread load.
    /// 利用过期缓存超时时间段解耦获取经过验证的坐标，分担系统线程负载。
    /// </summary>
    /// <param name="imageRegion">The tracked visual sector. 视觉跟踪扇区区域。</param>
    /// <param name="mapName">Map zone hierarchy label. 地图分级层级标签。</param>
    /// <param name="mapMatchingMethod">Underlying visual parsing engine. 底层视觉解析引擎。</param>
    /// <param name="cacheTimeMs">Threshold limits avoiding frame lock. 避免帧锁死的阈值界限。</param>
    /// <returns>Coordinate output derived from cache or heavy scan. 源自缓存或重扫描的坐标输出。</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="imageRegion"/> is null. 当 <paramref name="imageRegion"/> 为 null 时抛出。</exception>
    public Point2f GetPositionStableByCache(ImageRegion imageRegion, string mapName, string mapMatchingMethod, int cacheTimeMs = 900)
    {
        ArgumentNullException.ThrowIfNull(imageRegion);
        
        var captureTime = DateTime.UtcNow;
        if ((captureTime - _captureTime).TotalMilliseconds < cacheTimeMs && _prevX > 0f && _prevY > 0f)
        {
            return new Point2f(_prevX, _prevY);
        }

        return GetPositionStable(imageRegion, mapName, mapMatchingMethod);
    }
}