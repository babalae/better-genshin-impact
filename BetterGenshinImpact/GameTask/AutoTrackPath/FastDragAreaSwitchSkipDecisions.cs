namespace BetterGenshinImpact.GameTask.AutoTrackPath;

using System;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>
/// 快速拖动传送（TpTaskFastDrag）非提瓦特地图"跳过切换地区"决策。
/// teleport-fastdrag-skip-last-successful-map spec（bugfix.md BC-1~6）。
/// 纯函数、无副作用，PBT 友好。
/// </summary>
public static class FastDragAreaSwitchSkipDecisions
{
    /// <summary>
    /// 是否跳过 SwitchArea 直接识别定位：
    /// 仅当 首次尝试（retryTimes == 0）且 上次传送成功落地到同一张非提瓦特地图
    /// （lastSuccessfulMapName == targetMapName）时跳过
    /// （对标公版 TpTaskOfficial.s_lastSuccessfulTeleportMapName，L537/L548-549）。
    /// 重试轮（retryTimes &gt;= 1）恒不跳过（恢复无条件切区，保证最坏情况可切对，CC2）。
    /// Teyvat 目标排除：走 SwitchRecentlyCountryMap，不涉及 SwitchArea（对标公版 L548-549）。
    /// 任务结束 finally 清空标记 → 跨任务首传自动保守走切区（BC-1/BC-4）。
    /// </summary>
    public static bool ShouldSkipAreaSwitch(int retryTimes, string? lastSuccessfulMapName, string targetMapName)
        => retryTimes == 0
           && targetMapName != MapTypes.Teyvat.ToString()
           && string.Equals(lastSuccessfulMapName, targetMapName, StringComparison.Ordinal);
}