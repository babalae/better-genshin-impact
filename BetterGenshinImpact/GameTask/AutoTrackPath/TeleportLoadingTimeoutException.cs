using System;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 阶段 1（传送过渡页观察）超时异常。
///
/// 由 TpTask.WaitForTeleportCompletion 在 requireLoadingScreen=true 且 6s 内未观察到
/// 过渡页时抛出。会被 TpTask.Tp 的 catch (Exception) 分支捕获 → 触发 for (i&lt;3) retry。
///
/// 详见 .kiro/specs/multiplayer-tp-success-via-loading-screen/bugfix.md §"EB 2.9" / §"Open Question Q5"。
/// </summary>
public class TeleportLoadingTimeoutException : Exception
{
    public TeleportLoadingTimeoutException(string message) : base(message) { }

    public TeleportLoadingTimeoutException(string message, Exception innerException)
        : base(message, innerException) { }
}
