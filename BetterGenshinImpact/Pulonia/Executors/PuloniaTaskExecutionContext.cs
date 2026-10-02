using System;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 传给具体执行器的一次运行上下文；步骤 3 不包含游戏会话或输入所有权。
/// </summary>
public sealed class PuloniaTaskExecutionContext
{
    /// <summary>
    /// 本次提交请求 ID。
    /// </summary>
    public Guid RequestId { get; }

    /// <summary>
    /// 本次运行 ID。
    /// </summary>
    public Guid RunId { get; }

    /// <summary>
    /// 提交时固定的完整运行快照。
    /// </summary>
    public PuloniaTaskSnapshot Snapshot { get; }

    /// <summary>
    /// 当前节点从 1 开始的尝试次数。
    /// </summary>
    public int Attempt { get; }

    /// <summary>
    /// 建立一个节点执行上下文。
    /// </summary>
    internal PuloniaTaskExecutionContext(Guid requestId, Guid runId, PuloniaTaskSnapshot snapshot, int attempt)
    {
        RequestId = requestId;
        RunId = runId;
        Snapshot = snapshot;
        Attempt = attempt;
    }
}
