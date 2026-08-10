namespace BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;

/// <summary>
/// 精确接近路径点的结束原因。
/// </summary>
public enum PreciseMovementResult
{
    Running,
    Reached,
    EndAction,
    PositionLost,
    TimedOut,
    ResumeRecovery
}
