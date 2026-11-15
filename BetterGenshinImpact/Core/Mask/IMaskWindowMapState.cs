using System.Windows;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 遮罩窗口地图点位区域的状态入口（业务侧）。线程安全，立即返回
/// </summary>
public interface IMaskWindowMapState
{
    /// <summary>
    /// 字段为 null 表示不修改；多次调用合并为一个快照
    /// </summary>
    void Update(bool? isInBigMap = null, Rect? bigMapViewport = null, Rect? miniMapViewport = null,
        string? bigMapDebugText = null, long? bigMapFrameTick = null, long? bigMapRequestTick = null,
        MapTypes? mapType = null);

    /// <summary>
    /// 退出大地图并清空两个视口。在任务开始、地图遮罩关闭时调用
    /// </summary>
    void Reset();
}

/// <summary>
/// 地图点位区域的不可变快照
/// </summary>
/// <param name="IsInBigMap">是否在大地图界面</param>
/// <param name="BigMapViewport">大地图视口（2048 级地图坐标），空矩形表示不显示</param>
/// <param name="MiniMapViewport">小地图视口，空矩形表示不显示</param>
/// <param name="BigMapDebugText">大地图调试信息文本，null 表示无</param>
/// <param name="BigMapFrameTick">截图进入触发器的单调时钟，0 表示没有计时。</param>
/// <param name="BigMapRequestTick">本次定位请求起点，包含稳定等待、取消回收及重试，0 表示没有计时。</param>
/// <param name="MapType">两个视口及点位投影共同使用的场景坐标系。</param>
public sealed record MaskWindowMapSnapshot(bool IsInBigMap, Rect BigMapViewport, Rect MiniMapViewport,
    string? BigMapDebugText = null, long BigMapFrameTick = 0, long BigMapRequestTick = 0, MapTypes MapType = MapTypes.Teyvat)
{
    public static MaskWindowMapSnapshot Empty { get; } = new(false, default, default, null);
}
