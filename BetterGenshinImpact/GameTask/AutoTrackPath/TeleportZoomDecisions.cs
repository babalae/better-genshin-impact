using System;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// MoveMapTo 主移动循环放大决策（纯函数，便于属性测试）。
/// 修复：快速拖动模式下 mouseDistance 已进入收工区间时不应再触发对定位无意义的放大。
/// </summary>
public static class TeleportZoomDecisions
{
    /// <summary>
    /// 快速拖动模式收工阈值（与 MoveMapTo 收工分支②的阈值表达式同源同值）。
    /// retryTimes == 0 时为 400，否则为 300。
    /// </summary>
    public static int FastModeBreakThreshold(int retryTimes) => retryTimes == 0 ? 400 : 300;

    /// <summary>
    /// 判定本轮循环是否应执行地图放大。
    /// 等价于原放大分支的三重进入条件 (mapZoomEnabled||mapMoveStepDivisor)
    /// && mouseDistance &lt; (mapMoveStepDivisor?600:mapZoomInDistance)
    /// && currentZoomLevel &gt; minZoomLevel + precisionThreshold，
    /// 外加快速拖动模式前置门控：已进入收工区间(mouseDistance &lt; 收工阈值)
    /// 且当前缩放已在传送点可见档(currentZoomLevel &lt;= displayTpPointZoomLevel + precisionThreshold)时不放大。
    /// 缩放语义：值越小越放大；普通传送点仅在缩放 &lt;= 显示档(DisplayTpPointZoomLevel=4.4)时才渲染，
    /// 缩放仍大于显示档时即使距离到位也必须继续放大到可见档，否则会点在不显示传送点的空位上
    /// （神像/秘境全缩放可见，此处保守不区分点类型，多放大一次无害）。
    /// 经典模式(mapMoveStepDivisor == false)门控恒为 false，行为与原逻辑逐字节等价。
    /// </summary>
    public static bool ShouldZoomInThisIteration(
        bool mapMoveStepDivisor,
        bool mapZoomEnabled,
        double mouseDistance,
        double currentZoomLevel,
        double minZoomLevel,
        double precisionThreshold,
        int retryTimes,
        double mapZoomInDistance,
        double displayTpPointZoomLevel)
    {
        // 快速拖动模式：已进入收工区间 且 当前缩放已在传送点可见档 → 本轮不放大，落到收工 break 去点击。
        // 若缩放仍大于显示档(普通传送点不渲染)，即使距离到位也不跳过，继续放大到可见档，避免点空。
        // && mapMoveStepDivisor 保证经典模式此门控恒 false（零回归）。
        bool fastModeReadyToClick =
            mapMoveStepDivisor
            && mouseDistance < FastModeBreakThreshold(retryTimes)
            && currentZoomLevel <= displayTpPointZoomLevel + precisionThreshold;
        if (fastModeReadyToClick)
        {
            return false;
        }

        if (!(mapZoomEnabled || mapMoveStepDivisor))
        {
            return false;
        }

        double zoomInThreshold = mapMoveStepDivisor ? 600 : mapZoomInDistance;
        if (!(mouseDistance < zoomInThreshold))
        {
            return false;
        }

        return currentZoomLevel > minZoomLevel + precisionThreshold;
    }
}
