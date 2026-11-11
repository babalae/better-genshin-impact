using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 本进程写入或清除了某个账户的账户进度（完成记录或首领领奖次数）后发布的事件参数。
/// </summary>
/// <param name="accountId">进度发生变化的账户 ID</param>
public sealed class PuloniaAccountProgressChangedEventArgs(string accountId) : EventArgs
{
    /// <summary>进度发生变化的账户 ID。</summary>
    public string AccountId { get; } = accountId;
}
