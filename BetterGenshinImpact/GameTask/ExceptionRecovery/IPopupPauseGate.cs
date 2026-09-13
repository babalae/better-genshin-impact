namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 弹窗处理期间的挂起来源。与 INetworkPauseGate 对称，由 IPauseCoordinator 统一聚合。
/// </summary>
public interface IPopupPauseGate
{
    /// <summary>是否处于弹窗处理挂起</summary>
    bool IsPopupPaused { get; }

    /// <summary>进入挂起的原因，仅在 <see cref="IsPopupPaused"/> 为真时有效</summary>
    string? LastReason { get; }

    void EnterPopupPause(string reason);

    void ClearPopupPause();
}
