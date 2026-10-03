using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 传给具体执行器的一次运行上下文；步骤 3 不包含游戏会话或输入所有权。
/// </summary>
public sealed class PuloniaTaskExecutionContext
{
    /// <summary>
    /// 将完成事件直接提交到状态文件的宿主回调。
    /// </summary>
    private readonly Func<PuloniaTaskCompletionEvent, CancellationToken, Task> _completionReporter;

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
    /// 当前快照节点地址。
    /// </summary>
    public string TaskAddress { get; }

    /// <summary>
    /// 建立一个节点执行上下文。
    /// </summary>
    internal PuloniaTaskExecutionContext(Guid requestId, Guid runId, PuloniaTaskSnapshot snapshot, int attempt,
        string taskAddress, Func<PuloniaTaskCompletionEvent, CancellationToken, Task> completionReporter)
    {
        RequestId = requestId;
        RunId = runId;
        Snapshot = snapshot;
        Attempt = attempt;
        TaskAddress = taskAddress;
        _completionReporter = completionReporter;
    }

    /// <summary>
    /// 立即持久化一条带证据的幂等完成事件；写入失败会阻止当前节点继续成功收尾。
    /// </summary>
    public Task ReportCompletionAsync(PuloniaTaskCompletionEvent completionEvent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(completionEvent);
        return _completionReporter(completionEvent, ct);
    }
}
