namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 快速拖动传送（TpTaskFastDrag）的网络挂起信号检测器（本机实现）。
/// 茶包版转发共享 TaskControl.IsSuspendedByNetwork（联机断线挂起信号）；公版无网络挂起概念，
/// 本机化恒返回 false（安全默认）：TeleportLoadingPhaseSuspendGuard.ShouldSkip 仅由用户主动暂停
/// （RunnerContext.IsSuspend）触发跳过，传送过渡页守卫行为与单机语义一致。
/// </summary>
public static class TpTeleportSuspendDetector
{
    /// <summary>
    /// 是否因网络中断而挂起（暂停传送过渡页守卫）。公版无网络挂起概念，恒 false（见类注释）。
    /// </summary>
    public static bool IsSuspendedByNetwork => false;
}
