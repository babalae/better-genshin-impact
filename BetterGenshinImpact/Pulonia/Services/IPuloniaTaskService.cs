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
    /// 启动恢复提示：备份回退、上次未正常结束的运行、被跳过的损坏历史文件、无法写入历史的运行；没有这些事件时为空。
    /// </summary>
    string? RecoveryNotice { get; }

    /// <summary>
    /// 任一运行状态变化后触发；订阅方应再读取不可变状态视图。
    /// </summary>
    event EventHandler<PuloniaTaskRunChangedEventArgs>? RunChanged;

    /// <summary>
    /// 固定已保存计划的运行快照并加入串行队列，返回运行 ID（形如 <c>run_yyyyMMdd-HHmmss-xxxx</c>）。
    /// </summary>
    Task<string> EnqueueAsync(PuloniaTaskRequest request, CancellationToken ct = default);

    /// <summary>
    /// 接受主实例一步下发（<c>pulonia.run.submit</c>）的运行：读取共享计划，修订号与 <paramref name="planRevision"/> 不一致时拒绝；
    /// 一致时与本地提交相同地固定快照、放入本实例队列，来源记为 <see cref="PuloniaTaskRequest.DispatchSource"/>。
    /// 运行账户在运行开始时按本实例的当前账户或计划指定账户确定。
    /// </summary>
    /// <param name="planId">计划 ID。</param>
    /// <param name="planRevision">主实例保存后的计划修订号。</param>
    /// <param name="targetTaskId">执行范围；为空表示整个计划。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新运行 ID。</returns>
    /// <exception cref="InvalidOperationException">计划已变更（“计划已变更，请重新运行。”）。</exception>
    Task<string> EnqueueDispatchedAsync(string planId, long planRevision, string? targetTaskId, CancellationToken ct = default);

    /// <summary>
    /// 查询指定运行的最新不可变状态。
    /// </summary>
    Task<PuloniaTaskRunView> GetRunAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// 按提交时间倒序列出当前状态与已经落盘的不可变历史；历史视图的 <see cref="PuloniaTaskRunView.SnapshotJson"/> 在首次读取时才从历史文件加载。
    /// </summary>
    Task<IReadOnlyList<PuloniaTaskRunView>> ListRunsAsync(CancellationToken ct = default);

    /// <summary>
    /// 等待运行进入最终状态；调用方令牌只取消本次等待，不取消运行。
    /// </summary>
    Task<PuloniaTaskRunView> WaitForCompletionAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// 显式取消排队或正在执行的运行，并等待当前执行器确认退出。
    /// </summary>
    Task CancelAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// 只发出取消、不等待执行器退出：排队请求直接转为已取消并归档，执行中的运行转为停止中；已结束的运行忽略。
    /// 供跨实例取消（<c>pulonia.run.cancel</c>）使用，避免实例通信的响应等待运行结束。
    /// </summary>
    /// <param name="runId">运行 ID。</param>
    /// <param name="ct">取消令牌。</param>
    /// <exception cref="KeyNotFoundException">运行不存在。</exception>
    Task RequestCancelAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// 沿用历史运行的固定快照继续未完成节点，或明确从指定节点开始；返回新运行 ID。
    /// </summary>
    Task<string> ResumeAsync(string runId, string? startTaskAddress = null,
        bool allowUncertainReplay = false, CancellationToken ct = default);

    // 完成记录属于账户进度：读取、清除与变化通知见 IPuloniaAccountService。

    /// <summary>
    /// 按已保存计划的当前配置预测一次运行：与提交相同地准备快照，但不入队、不写运行状态。
    /// 每个叶子给出执行、禁用、因路线 CD 跳过或本次重复的预测，运行到节点时仍会重新判定。
    /// 读取哪个账户的完成记录：<paramref name="accountId"/> 不为空时为该账户；否则计划指定账户时为该账户，
    /// 跟随当前账户时为本实例的当前账户，未确定时退回账户列表第一个并在结果中说明。日切按该账户的服务器时区。
    /// </summary>
    /// <param name="request">运行请求。</param>
    /// <param name="accountId">界面临时切换查看的账户 ID；为空时按计划的运行账户设置选择。</param>
    /// <param name="ct">取消令牌。</param>
    Task<PuloniaTaskRunPreview> PreviewAsync(PuloniaTaskRequest request, string? accountId = null, CancellationToken ct = default);

    /// <summary>
    /// 按当前计划检查定时触发器并提交到点的请求；每次恢复只合并仍可延迟启动的最新一次，不补跑整段离线历史。
    /// </summary>
    /// <param name="plans">当前全部可读取的计划；不在其中的计划或定时触发器的游标会被剔除，调用方不能只传部分计划。</param>
    /// <param name="unreadablePlanIds">本轮读取失败的计划 ID，其游标与反馈保留不动；为空表示没有读取失败的计划。</param>
    /// <param name="ct">取消信号。</param>
    Task CheckTriggersAsync(IReadOnlyList<PuloniaTaskPlan> plans, IReadOnlyCollection<string>? unreadablePlanIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// 处理一次热键触发：与定时触发使用相同准入和忙碌策略，1 秒内重复按下只算一次，不写调度游标。
    /// </summary>
    /// <param name="planId">热键所属计划 ID。</param>
    /// <param name="triggerId">热键触发器 ID。</param>
    /// <param name="ct">取消信号。</param>
    Task FireHotkeyAsync(string planId, string triggerId, CancellationToken ct = default);

    /// <summary>
    /// 取得各启用触发器最近一次处理结果的独立副本；关联运行的状态与消息按当前运行记录读取。
    /// </summary>
    IReadOnlyList<PuloniaTaskTriggerStatus> GetTriggerStatuses();
}
