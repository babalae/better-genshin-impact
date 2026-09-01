using System;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 拖动主循环"中心点连续识别异常"时的分级补救决策（teleport-drag-center-recognition-escalating-recovery spec）。
///
/// 背景：MoveMapTo 拖动循环识别中心点连续异常时，现状第 1 次盲走推算延续错误中心、第 2 次直接抛"重新传送"，
/// 最终依赖"拖到边缘→亮度过低→切区域"被动救回（多耗数秒且靠运气）。本类把"怎么补救"抽成纯函数：
///   第 1 级盲走（现状）→ 第 2 级拉大缩放再识别 → 第 3 级切地区 → 兜底抛重传。
///
/// 仅提瓦特连续大图启用（IsRecoveryApplicable），独立地图走旧逻辑，零回归。
/// 纯函数无副作用，PBT 可撒输入守护。
/// </summary>
public enum CenterRecoveryAction
{
    /// <summary>无动作（正常路径，不进入补救）。</summary>
    None,

    /// <summary>第 1 级：盲走推算，延续预测中心点（现状行为）。</summary>
    BlindWalk,

    /// <summary>第 2 级：当前缩放小于稳定档 → 拉大缩放后重新识别。</summary>
    ZoomInThenRecog,

    /// <summary>第 3 级：切地区后重新识别。</summary>
    SwitchAreaThenRecog,

    /// <summary>兜底：仍失败，抛"重新传送"异常。</summary>
    ThrowRetry,
}

/// <summary>拖动中心点识别异常的分级补救决策（纯函数，PBT 友好）。</summary>
public static class AutoTrackPositionRecoveryDecisions
{
    /// <summary>
    /// 第 2 级触发"拉大缩放再识别"的缩放下限（识别稳档）。
    /// 缩放数字越大越缩小、越小越放大（地图视野）。当前缩放 &lt; 此值视为"小缩放/放大视野"（易认错），拉大到此档。
    /// 与传送定位循环的识别稳档 DisplayTpPointZoomLevel(4.4) 一致。
    /// </summary>
    public const double RecoverStableZoom = 4.4;

    /// <summary>
    /// 分级补救是否适用。仅提瓦特连续大图启用；独立地图（旧日之海/霜月等）返回 false，走旧逻辑零回归。
    /// </summary>
    public static bool IsRecoveryApplicable(bool isTeyvat) => isTeyvat;

    /// <summary>
    /// 根据连续识别异常次数 + 当前缩放，决定分级补救动作。
    /// </summary>
    /// <param name="times">已累计的连续中心点识别异常次数（≥1；由 MoveMapTo catch 分支自增）。</param>
    /// <param name="currentZoom">当前地图缩放档位。</param>
    public static CenterRecoveryAction Decide(int times, double currentZoom)
    {
        switch (times)
        {
            case 1:
                return CenterRecoveryAction.BlindWalk;
            case 2:
                // 缩放 < 稳定档 → 拉大再识别；已 ≥ 稳定档（非缩放问题）→ 直接切地区
                return currentZoom < RecoverStableZoom
                    ? CenterRecoveryAction.ZoomInThenRecog
                    : CenterRecoveryAction.SwitchAreaThenRecog;
            case 3:
                return CenterRecoveryAction.SwitchAreaThenRecog;
            default:
                return CenterRecoveryAction.ThrowRetry;
        }
    }
}
