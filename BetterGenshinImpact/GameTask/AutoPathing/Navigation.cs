using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using BetterGenshinImpact.GameTask.Common.Map.Maps;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.GameTask.AutoPathing;

/// <summary>
/// Static facade for the global navigation system. Provides pure mathematical calculations and low-level map matching calls.
/// 全局导航系统的静态外观模式门面。提供纯粹的数学计算及底层的地图匹配调用。
/// </summary>
public class Navigation
{
    /// <summary>
    /// The cached identifier of the last used map matching methodology.
    /// 最后一次使用的地图匹配算法的缓存标识符。
    /// </summary>
    private static string? _lastMapMatchMethod = null;
    
    /// <summary>
    /// The underlying thread-safe singleton instance for navigation state management.
    /// 用于导航状态管理的底层线程安全单例实例。
    /// </summary>
    private static readonly NavigationInstance _instance = new();

    /// <summary>
    /// Initializes map cache and triggers warm-up phase to avoid cold-start latency.
    /// 初始化地图缓存并触发预热阶段，避免冷启动造成的计算卡顿。
    /// </summary>
    /// <param name="mapMatchMethod">The map matching algorithm identifier. 地图匹配算法的标识符。</param>
    public static void WarmUp(string mapMatchMethod)
    {
        if (string.IsNullOrEmpty(mapMatchMethod)) return;

        if (!string.Equals(_lastMapMatchMethod, mapMatchMethod, StringComparison.OrdinalIgnoreCase))
        {
            MapManager.GetMap(MapTypes.Teyvat, mapMatchMethod).WarmUp();
            _lastMapMatchMethod = mapMatchMethod;
        }

        Reset();
    }

    /// <summary>
    /// Flushes and resets the underlying coordinate tracking buffer.
    /// 刷新并重置底层的坐标追踪缓存。
    /// </summary>
    public static void Reset()
    {
        _instance.Reset();
    }
    
    /// <summary>
    /// Overrides the internal kinematic state history with absolute coordinates.
    /// 使用绝对坐标覆盖内部的运动学状态历史记录。
    /// </summary>
    /// <param name="x">The absolute map X-coordinate. 地图的绝对 X 坐标。</param>
    /// <param name="y">The absolute map Y-coordinate. 地图的绝对 Y 坐标。</param>
    public static void SetPrevPosition(float x, float y)
    {
        // Prevents NaN poisoning 阻断 NaN 污染
        if (float.IsNaN(x) || float.IsNaN(y)) return;
        _instance.SetPrevPosition(x, y);
    }

    /// <summary>
    /// Computes the fast realtime localized position based on a sliding window algorithm.
    /// 基于滑动窗口算法计算快速实时的本地化位置。
    /// </summary>
    /// <param name="imageRegion">The captured visual surface context buffer. 捕获的视觉表面图像区域缓冲。</param>
    /// <param name="mapName">Name of the target logical region map. 目标逻辑区域地图名称。</param>
    /// <param name="mapMatchMethod">The configured computer vision matching methodology. 配置的计算机视觉匹配方法。</param>
    /// <returns>Computed position as a dual-component vector. 计算得到的位置，表示为双分量向量。</returns>
    public static Point2f GetPosition(ImageRegion imageRegion, string mapName, string mapMatchMethod)
    {
        ArgumentNullException.ThrowIfNull(imageRegion);
        return _instance.GetPosition(imageRegion, mapName, mapMatchMethod);
    }

    /// <summary>
    /// Computes strictly validated absolute global position, heavily penalizing frame latency but guaranteeing spatial context.
    /// 稳定获取当前位置坐标，优先使用全地图匹配，适用于不需要高效率但需要高稳定性的断点恢复场景。
    /// </summary>
    /// <param name="imageRegion">The captured visual surface context buffer. 视觉表面图像区域缓冲。</param>
    /// <param name="mapName">The target map area context. 目标地图区域上下文。</param>
    /// <param name="mapMatchMethod">The matching model behavior identifier. 匹配模型行为标识符。</param>
    /// <returns>The rigorously validated spatial coordinate. 经过严格验证的空间全局坐标。</returns>
    public static Point2f GetPositionStable(ImageRegion imageRegion, string mapName, string mapMatchMethod)
    {
        ArgumentNullException.ThrowIfNull(imageRegion);
        return _instance.GetPositionStable(imageRegion, mapName, mapMatchMethod);
    }

    /// <summary>
    /// Calculates the required yaw orientation in degrees to align the entity towards the destination waypoint.
    /// 计算将实体对齐至目的路点所需偏航角（以度为单位）。
    /// </summary>
    /// <param name="waypoint">The ultimate destination spatial target. 终极空间定位目标。</param>
    /// <param name="position">The current origin spatial coordinates. 当前源空间坐标。</param>
    /// <returns>Integer angle spanning [0, 360). 跨越 [0, 360) 的整数角度。</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="waypoint"/> is null. 当 <paramref name="waypoint"/> 为 null 时抛出。</exception>
    public static int GetTargetOrientation(Waypoint waypoint, Point2f position)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        
        double deltaY = waypoint.Y - position.Y;
        double deltaX = waypoint.X - position.X;
        
        double angle = Math.Atan2(deltaY, deltaX);
        if (angle < 0)
        {
            angle += 2 * Math.PI;
        }

        return (int)Math.Round(angle * (180.0 / Math.PI)) % 360;
    }

    /// <summary>
    /// Computes the Euclidean scalar distance between the origin coordinates and the target waypoint.
    /// 计算源坐标与目标路点之间的欧几里得标量距离。
    /// </summary>
    /// <param name="waypoint">The destination target coordinates. 终点目标坐标。</param>
    /// <param name="position">The spatial origin coordinates. 空间源坐标。</param>
    /// <returns>Geometric linear span difference. 几何线性跨距差异。</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="waypoint"/> is null. 当 <paramref name="waypoint"/> 为 null 时抛出。</exception>
    public static double GetDistance(Waypoint waypoint, Point2f position)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        
        double dx = position.X - waypoint.X;
        double dy = position.Y - waypoint.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
