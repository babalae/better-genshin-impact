namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

/// <summary>
/// 当前活动窗口不是游戏窗口。该异常应交给任务运行层处理，业务任务不应据此重试自身流程。
/// </summary>
public sealed class GameWindowNotFocusedException : RetryException
{
    public GameWindowNotFocusedException(string message) : base(message)
    {
    }
}
