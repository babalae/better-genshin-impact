using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 本进程修改了某个实例的当前账户后发布的事件参数。
/// </summary>
/// <param name="instanceKey">被修改的实例键</param>
/// <param name="accountId">新的当前账户 ID；为空表示“未确定”</param>
public sealed class PuloniaCurrentAccountChangedEventArgs(string instanceKey, string? accountId) : EventArgs
{
    /// <summary>被修改的实例键。</summary>
    public string InstanceKey { get; } = instanceKey;

    /// <summary>新的当前账户 ID；为空表示“未确定”。</summary>
    public string? AccountId { get; } = accountId;
}
