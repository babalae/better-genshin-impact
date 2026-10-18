using System.Windows;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 遮罩窗口地图点位区域的状态入口（业务侧）。线程安全，立即返回
/// </summary>
public interface IMaskWindowMapState
{
    /// <summary>
    /// 字段为 null 表示不修改；多次调用合并为一个快照
    /// </summary>
    void Update(bool? isInBigMap = null, Rect? bigMapViewport = null, Rect? miniMapViewport = null);

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
public sealed record MaskWindowMapSnapshot(bool IsInBigMap, Rect BigMapViewport, Rect MiniMapViewport)
{
    public static MaskWindowMapSnapshot Empty { get; } = new(false, default, default);
}
