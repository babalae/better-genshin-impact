namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 阶段 1（传送过渡页观察）暂停 / 网络断开早退守卫。
///
/// 用于解决 .kiro/specs/multiplayer-tp-loading-screen-suspend-skip/bugfix.md 描述的
/// "墙钟 deadline 在暂停期间继续累积 → 解除暂停后立即超时 → 误抛 TeleportLoadingTimeoutException"
/// 问题。
///
/// 设计为静态纯函数：仅依赖两个布尔输入，无外部依赖（不读 RunnerContext / TaskControl，
/// 不持有 logger，不触发截图），便于 Property-Based Test 直接撒 (isSuspend, isSuspendedByNetwork)
/// 四种组合验证。
///
/// 与 bgi-implementation-patterns.md §1 "决策函数纯化" 模式一致。
/// </summary>
internal static class TeleportLoadingPhaseSuspendGuard
{
    /// <summary>
    /// 任一标志为 true 时返回 true（应早退跳过阶段 1，回退到阶段 2 派蒙判据）。
    /// </summary>
    /// <param name="isSuspend">RunnerContext.Instance.IsSuspend 当前值</param>
    /// <param name="isSuspendedByNetwork">TaskControl.IsSuspendedByNetwork 当前值</param>
    /// <returns>true = 应早退；false = 继续正常轮询过渡页</returns>
    public static bool ShouldSkip(bool isSuspend, bool isSuspendedByNetwork)
    {
        return isSuspend || isSuspendedByNetwork;
    }
}
