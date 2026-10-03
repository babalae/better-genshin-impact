using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// Pulonia 计划提交、状态查询、等待和取消的进程内统一入口。
/// </summary>
public interface IPuloniaTaskService
{
    /// <summary>
    /// 当前进程已经注册的任务能力说明，供任务创建和参数编辑界面复用。
    /// </summary>
    IReadOnlyList<PuloniaTaskDefinition> Definitions { get; }

    /// <summary>
    /// 启动恢复或备份回退提示；没有恢复事件时为空。
    /// </summary>
    string? RecoveryNotice { get; }

    /// <summary>
    /// 任一运行状态变化后触发；订阅方应再读取不可变状态视图。
    /// </summary>
    event EventHandler<PuloniaTaskRunChangedEventArgs>? RunChanged;

    /// <summary>
    /// 固定已保存计划的运行快照并加入串行队列，返回请求 ID。
    /// </summary>
    Task<Guid> EnqueueAsync(PuloniaTaskRequest request, CancellationToken ct = default);

    /// <summary>
    /// 查询指定请求的最新不可变运行状态。
    /// </summary>
    Task<PuloniaTaskRunView> GetRunAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// 按提交时间倒序列出当前状态与已经落盘的不可变历史。
    /// </summary>
    Task<IReadOnlyList<PuloniaTaskRunView>> ListRunsAsync(CancellationToken ct = default);

    /// <summary>
    /// 等待请求进入最终状态；调用方令牌只取消本次等待，不取消运行。
    /// </summary>
    Task<PuloniaTaskRunView> WaitForCompletionAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// 显式取消排队或正在执行的请求，并等待当前执行器确认退出。
    /// </summary>
    Task CancelAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// 沿用历史运行的固定快照继续未完成节点，或明确从指定节点开始。
    /// </summary>
    Task<Guid> ResumeAsync(Guid runId, string? startTaskAddress = null,
        bool allowUncertainReplay = false, CancellationToken ct = default);

    /// <summary>
    /// 读取当前仍约束执行的 CD 与额度账本副本。
    /// </summary>
    Task<IReadOnlyList<PuloniaTaskLedgerEntry>> ListLedgerAsync(CancellationToken ct = default);
}
