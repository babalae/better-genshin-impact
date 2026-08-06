using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 战斗主动结束并将后续恢复交还给调用方处理。
/// </summary>
public sealed class CombatInterruptionException : RetryException
{
    public CombatInterruptionException(CombatInterruptionReason reason, string message) : base(message)
    {
        Reason = reason;
    }

    public CombatInterruptionReason Reason { get; }
}

public enum CombatInterruptionReason
{
    Swimming,
    Defeated,
    Timeout,
    TargetSearchExhausted
}
