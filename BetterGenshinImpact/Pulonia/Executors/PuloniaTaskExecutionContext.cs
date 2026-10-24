using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 传给具体执行器的一次运行上下文。游戏节点附带本节点租约记录的运行环境，纯任务节点为 null。
/// </summary>
public sealed class PuloniaTaskExecutionContext
{
    /// <summary>
    /// 将完成事件直接提交到状态文件的宿主回调。
    /// </summary>
    private readonly Func<PuloniaTaskCompletionEvent, CancellationToken, Task> _completionReporter;

    /// <summary>
    /// 本次运行 ID，形如 <c>run_yyyyMMdd-HHmmss-xxxx</c>。
    /// </summary>
    public string RunId { get; }

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
    /// 本节点租约记录的运行环境；不需要游戏会话的节点为 null。节点内的任务只使用它
    /// </summary>
    public GameRuntime? Runtime { get; }

    /// <summary>
    /// 建立一个节点执行上下文。
    /// </summary>
    internal PuloniaTaskExecutionContext(string runId, PuloniaTaskSnapshot snapshot, int attempt,
        string taskAddress, GameRuntime? runtime, Func<PuloniaTaskCompletionEvent, CancellationToken, Task> completionReporter)
    {
        RunId = runId;
        Snapshot = snapshot;
        Attempt = attempt;
        TaskAddress = taskAddress;
        Runtime = runtime;
        _completionReporter = completionReporter;
    }

    /// <summary>
    /// 取得本节点的运行环境，供执行器构造任务。
    /// 没有运行环境，或它已经停止（截图器被停止或重新绑定）时返回 false：节点据此失败，
    /// 下一节点重新获取租约，不会悄悄换到新的运行环境
    /// </summary>
    /// <param name="runtime">本节点的运行环境；返回 false 时为 null</param>
    /// <param name="failureMessage">不能使用时的失败原因；返回 true 时为 null</param>
    public bool TryGetRuntime([NotNullWhen(true)] out GameRuntime? runtime, [NotNullWhen(false)] out string? failureMessage)
    {
        if (Runtime is null)
        {
            runtime = null;
            failureMessage = "当前节点没有持有游戏输入租约，无法执行游戏任务。";
            return false;
        }

        if (Runtime.IsStopping)
        {
            runtime = null;
            failureMessage = "本节点使用的游戏运行环境已停止，当前节点失败；下一节点会重新准备运行环境。";
            return false;
        }

        runtime = Runtime;
        failureMessage = null;
        return true;
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
