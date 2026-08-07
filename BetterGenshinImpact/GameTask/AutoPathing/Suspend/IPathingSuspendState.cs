namespace BetterGenshinImpact.GameTask.AutoPathing.Suspend;

/// <summary>
/// 移动控制器需要感知的最小暂停状态。
/// </summary>
public interface IPathingSuspendState
{
    bool IsResumeRecoveryPending { get; }
}

internal sealed class NoOpPathingSuspendState : IPathingSuspendState
{
    public static NoOpPathingSuspendState Instance { get; } = new();

    public bool IsResumeRecoveryPending => false;

    private NoOpPathingSuspendState()
    {
    }
}
