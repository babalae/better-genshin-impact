using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 运行与节点结果的状态归类：是否需要核验、是否仍在活跃、运行状态分类与子树汇总状态。
/// 只做与界面无关的推导，展示文本和颜色由界面决定。
/// </summary>
public static class PuloniaTaskRunClassifier
{
    /// <summary>节点因自身或祖先关闭而未执行的原因码；界面据此把未启用的任务与真正的跳过分开展示。</summary>
    public const string DisabledReason = "task.disabled";

    /// <summary>续跑时节点已在原运行中完成或位于显式起点之前、因而未执行的原因码。</summary>
    public const string ResumeSkippedReason = "task.resume_skipped";

    /// <summary>
    /// 业务结果是否不确定：已执行未核验、部分成功或待人工核对，都需要核对游戏状态。
    /// </summary>
    public static bool IsUncertainOutcome(PuloniaTaskOutcomeKind kind)
        => kind is PuloniaTaskOutcomeKind.ExecutedUnverified or PuloniaTaskOutcomeKind.PartiallySucceeded
            or PuloniaTaskOutcomeKind.NeedsAttention;

    /// <summary>
    /// 任一尝试的业务结果不确定时提示人工核验；重试前的尝试同样计入，避免掩盖曾经发生的不确定副作用。
    /// </summary>
    public static bool HasUncertainAttempt(IEnumerable<PuloniaTaskNodeResult> results)
        => results.Any(item => IsUncertainOutcome(item.OutcomeKind));

    /// <summary>
    /// 沿用历史快照重放前是否需要用户明确承担不确定结果：运行待处理，或任一节点最后一次尝试的结果不确定。
    /// </summary>
    public static bool RequiresReplayConfirmation(PuloniaTaskRunStatus status, IEnumerable<PuloniaTaskNodeResult> results)
        => status == PuloniaTaskRunStatus.NeedsAttention
           || results.GroupBy(item => item.TaskAddress, StringComparer.Ordinal)
               .Select(group => group.Last())
               .Any(item => IsUncertainOutcome(item.OutcomeKind));

    /// <summary>
    /// 运行是否仍未结束（排队、运行中或停止中），此时仍可请求取消。
    /// </summary>
    public static bool IsActive(PuloniaTaskRunStatus status)
        => status is PuloniaTaskRunStatus.Queued or PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling;

    /// <summary>
    /// 把运行状态归入展示用的语义分类。
    /// </summary>
    public static PuloniaTaskRunStatusCategory Categorize(PuloniaTaskRunStatus status) => status switch
    {
        // 停止中仍属于未结束的活跃状态，与运行中一样延续强调色。
        PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling => PuloniaTaskRunStatusCategory.Running,
        PuloniaTaskRunStatus.Succeeded => PuloniaTaskRunStatusCategory.Success,
        // 超时按失败处理，与状态筛选的“失败 / 超时”分组保持一致。
        PuloniaTaskRunStatus.Failed or PuloniaTaskRunStatus.TimedOut => PuloniaTaskRunStatusCategory.Failed,
        // 中断通常需要人工续跑或核对，与待处理一样使用警示色。
        PuloniaTaskRunStatus.NeedsAttention or PuloniaTaskRunStatus.Interrupted => PuloniaTaskRunStatusCategory.Attention,
        _ => PuloniaTaskRunStatusCategory.Neutral
    };

    /// <summary>
    /// 汇总一次运行中某个计划子树的状态。节点地址形如“计划 ID/根节点 ID/节点 ID/…”，
    /// 按完整前缀匹配，引用计划内部允许与其他计划使用相同的节点 ID，重复名称也不会互相串状态。
    /// </summary>
    /// <param name="run">要汇总的运行；为空表示尚无运行。</param>
    /// <param name="rootTaskId">运行计划的根节点 ID。</param>
    /// <param name="taskId">子树根节点 ID。</param>
    public static PuloniaTaskSubtreeStatus ClassifySubtree(PuloniaTaskRunView? run, string rootTaskId, string taskId)
        => run is null
            ? PuloniaTaskSubtreeStatus.NotRun
            : ClassifySubtree(run.PlanId + "/" + rootTaskId + "/" + taskId, run.Status, run.CurrentTaskAddress, run.NodeResults);

    /// <summary>
    /// 按完整地址前缀汇总子树状态；前缀本身或以“前缀/”开头的地址都属于该子树。
    /// </summary>
    /// <param name="addressPrefix">子树根节点的完整地址。</param>
    /// <param name="status">运行状态。</param>
    /// <param name="currentTaskAddress">运行当前或最近执行的节点地址。</param>
    /// <param name="nodeResults">运行已结束的全部节点尝试。</param>
    public static PuloniaTaskSubtreeStatus ClassifySubtree(string addressPrefix, PuloniaTaskRunStatus status,
        string? currentTaskAddress, IEnumerable<PuloniaTaskNodeResult> nodeResults)
    {
        bool Contains(string? address) => address is not null
            && (address == addressPrefix || address.StartsWith(addressPrefix + "/", StringComparison.Ordinal));
        var results = nodeResults.Where(item => Contains(item.TaskAddress)).ToArray();

        // 优先级：执行中 > 失败 > 待核验 > 全部完成 > 尚未执行 / 已跳过或停止
        if (status is PuloniaTaskRunStatus.Running or PuloniaTaskRunStatus.Cancelling && Contains(currentTaskAddress))
            return PuloniaTaskSubtreeStatus.Running;
        if (results.Any(item => item.Status is PuloniaTaskNodeStatus.Failed or PuloniaTaskNodeStatus.TimedOut))
            return PuloniaTaskSubtreeStatus.Failed;
        // 部分成功的节点状态是失败，已在上一步归类；这里只看待处理节点与未核验结果。
        if (results.Any(item => item.Status == PuloniaTaskNodeStatus.NeedsAttention
                                || item.OutcomeKind == PuloniaTaskOutcomeKind.ExecutedUnverified))
            return PuloniaTaskSubtreeStatus.NeedsVerification;
        if (results.Length > 0 && results.All(item => item.Status == PuloniaTaskNodeStatus.Succeeded))
            return PuloniaTaskSubtreeStatus.Completed;
        return results.Length == 0 ? PuloniaTaskSubtreeStatus.NotRun : PuloniaTaskSubtreeStatus.SkippedOrStopped;
    }
}
