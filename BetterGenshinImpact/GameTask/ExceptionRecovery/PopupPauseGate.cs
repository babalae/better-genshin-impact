namespace BetterGenshinImpact.GameTask.ExceptionRecovery;

/// <summary>
/// 弹窗处理挂起门。写者只有 GameExceptionPopupTrigger，读者是 IPauseCoordinator。
/// </summary>
public sealed class PopupPauseGate : IPopupPauseGate
{
    private readonly object _sync = new();
    private bool _isPopupPaused;
    private string? _lastReason;

    public bool IsPopupPaused
    {
        get
        {
            lock (_sync)
            {
                return _isPopupPaused;
            }
        }
    }

    public string? LastReason
    {
        get
        {
            lock (_sync)
            {
                return _lastReason;
            }
        }
    }

    public void EnterPopupPause(string reason)
    {
        lock (_sync)
        {
            _lastReason = reason;
            _isPopupPaused = true;
        }
    }

    public void ClearPopupPause()
    {
        lock (_sync)
        {
            _isPopupPaused = false;
            _lastReason = null;
        }
    }
}
