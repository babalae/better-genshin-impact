using System;

namespace BetterGenshinImpact.Pulonia.Models;

/// <summary>
/// 占用文件中写入的持有者：账户占用 <c>accounts/&lt;账户 ID&gt;/run.lock</c> 记录正在使用该账户的运行，
/// 在线标记 <c>instances/&lt;目录名&gt;/online.lock</c> 记录正在运行的实例进程。
/// 文件内容只用于提示“谁在占用”；是否占用以句柄是否仍被持有为准，进程退出后由系统释放，文件内容随之失效。
/// </summary>
public sealed class PuloniaLockOwner
{
    /// <summary>持有者所在实例的实例键。</summary>
    public string InstanceKey { get; init; } = string.Empty;

    /// <summary>占用账户的运行 ID；在线标记与账户页“测试切换”的占用没有运行 ID，为 null。</summary>
    public string? RunId { get; init; }

    /// <summary>持有者进程 ID，便于排查。</summary>
    public int ProcessId { get; init; }

    /// <summary>取得占用的时间（UTC）。</summary>
    public DateTimeOffset StartedAt { get; init; }
}
