using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 执行通道与跨实例派发：同一执行实例内串行，不同执行实例之间并行；
/// 远程运行仍由本协调器编排和落盘，只有叶子节点与账号准备交给子实例执行
/// </summary>
public sealed partial class PuloniaTaskService
{
    /// <summary>
    /// 把节点交给子实例执行的入口；没有注册时只能本地执行
    /// </summary>
    private readonly IPuloniaRemoteNodeDispatcher? _remoteDispatcher;

    /// <summary>
    /// 是否为唯一协调器。子实例中为 false：不读写 state.json、不消费队列、不接受提交，
    /// 避免与主实例同时改写共享的 User/Pulonia 状态
    /// </summary>
    private readonly bool _isCoordinator;

    /// <summary>
    /// 正在执行运行的通道，由 _runsGate 保护
    /// </summary>
    private readonly HashSet<string> _activeLanes = new(StringComparer.Ordinal);

    /// <summary>
    /// 正在执行的运行任务，关闭时等待全部退出
    /// </summary>
    private readonly ConcurrentDictionary<Guid, Task> _laneTasks = new();

    /// <summary>
    /// 返回运行所属的执行通道
    /// </summary>
    private static string GetLaneKey(RunState state) => PuloniaTaskExecutionTarget.GetLaneKey(state.Request.ExecutionTarget);

    /// <summary>
    /// 占用通道并在后台执行运行；结束后释放通道并唤醒消费循环挑选同一通道的下一个请求
    /// </summary>
    /// <param name="state">要执行的运行</param>
    /// <param name="lane">执行通道</param>
    private void StartLane(RunState state, string lane)
    {
        lock (_runsGate)
            _activeLanes.Add(lane);
        _laneTasks[state.RequestId] = Task.Run(async () =>
        {
            try
            {
                await ExecuteRunAsync(state).ConfigureAwait(false);
            }
            finally
            {
                lock (_runsGate)
                    _activeLanes.Remove(lane);
                _laneTasks.TryRemove(state.RequestId, out _);
                _queue.Writer.TryWrite(state);
            }
        });
    }

    /// <summary>
    /// 子实例不运行 Pulonia 计划
    /// </summary>
    /// <exception cref="InvalidOperationException">当前不是协调器</exception>
    private void ThrowIfNotCoordinator()
    {
        if (!_isCoordinator)
            throw new InvalidOperationException("子实例不能直接运行 Pulonia 计划，请在主实例的新一条龙页面选择本实例运行。");
    }

    /// <summary>
    /// 提交前确认执行实例可用，避免请求排队后才发现目标不在线
    /// </summary>
    /// <param name="request">已校验的请求</param>
    /// <exception cref="InvalidOperationException">不支持派发或目标不在线</exception>
    private void EnsureExecutionTargetAvailable(PuloniaTaskRequest request)
    {
        if (request.ExecutionTarget is not { } target)
            return;
        if (_remoteDispatcher is null)
            throw new InvalidOperationException("当前实例不支持把计划派发到子实例。");
        if (!_remoteDispatcher.IsAvailable(target))
            throw new InvalidOperationException($"执行实例“{target.DisplayName}”当前不在线。");
    }

    /// <summary>
    /// 同一计划同时只能在一个执行实例上排队或运行：账本按账号作用域判断，跨实例并行可能同时通过“每日一次”等检查
    /// </summary>
    /// <param name="request">要提交的请求</param>
    /// <exception cref="InvalidOperationException">计划已在其他执行实例上排队或运行</exception>
    private void EnsureNoOtherLaneForPlan(PuloniaTaskRequest request)
    {
        var lane = PuloniaTaskExecutionTarget.GetLaneKey(request.ExecutionTarget);
        RunState? conflict;
        lock (_runsGate)
            conflict = _runs.Values.FirstOrDefault(item => !item.IsHistorical && !item.IsTerminal()
                && item.Request.PlanId == request.PlanId
                && !string.Equals(GetLaneKey(item), lane, StringComparison.Ordinal));
        if (conflict is not null)
            throw new InvalidOperationException(
                $"该计划已在“{DescribeTarget(conflict.Request.ExecutionTarget)}”上排队或运行，同一配置同时只能在一个实例上执行。");
    }

    /// <summary>
    /// 执行实例的显示名
    /// </summary>
    private static string DescribeTarget(PuloniaTaskExecutionTarget? target)
        => target is null ? "主实例" : string.IsNullOrWhiteSpace(target.DisplayName) ? target.LaneKey : target.DisplayName;

    /// <summary>
    /// 把活动运行写入候选状态：主实例通道写 active_run，子实例通道写 remote_active_runs，每个通道最多一个
    /// </summary>
    /// <exception cref="InvalidOperationException">同一通道出现多个活动运行</exception>
    private static void AddActiveRecord(PuloniaTaskState candidate, PuloniaTaskRunRecord record)
    {
        if (record.Request.ExecutionTarget is not { } target)
        {
            if (candidate.ActiveRun is not null && candidate.ActiveRun.RunId != record.RunId)
                throw new InvalidOperationException("Pulonia 状态中出现多个活动运行。");
            candidate.ActiveRun = record;
            return;
        }

        if (candidate.RemoteActiveRuns.Any(item => item.RunId != record.RunId && target.IsSameLane(item.Request.ExecutionTarget)))
            throw new InvalidOperationException("Pulonia 状态中同一执行实例出现多个活动运行。");
        candidate.RemoteActiveRuns.RemoveAll(item => item.RunId == record.RunId);
        candidate.RemoteActiveRuns.Add(record);
    }

    /// <summary>
    /// 在候选状态中查找本运行的活动记录
    /// </summary>
    private static PuloniaTaskRunRecord? FindActiveRecord(PuloniaTaskState candidate, Guid runId)
        => candidate.ActiveRun?.RunId == runId
            ? candidate.ActiveRun
            : candidate.RemoteActiveRuns.FirstOrDefault(item => item.RunId == runId);

    /// <summary>
    /// 用新记录替换候选状态中的同一活动运行
    /// </summary>
    /// <exception cref="InvalidOperationException">活动运行已变化</exception>
    private static void ReplaceActiveRecord(PuloniaTaskState candidate, PuloniaTaskRunRecord record)
    {
        if (candidate.ActiveRun?.RunId == record.RunId)
        {
            candidate.ActiveRun = record;
            return;
        }

        var index = candidate.RemoteActiveRuns.FindIndex(item => item.RunId == record.RunId);
        if (index < 0)
            throw new InvalidOperationException("文件成功结算时活动运行已变化。");
        candidate.RemoteActiveRuns[index] = record;
    }

    /// <summary>
    /// 把一个叶子节点交给执行实例执行，并把结束回报转换为本地执行器的等价结果或异常：
    /// 账号失败转为待处理；执行实例本地停止时取消整次运行，不进入重试；其余失败按节点异常处理
    /// </summary>
    private async Task<PuloniaTaskOutcome> ExecuteRemoteNodeAsync(RunState state, PuloniaTaskPreparedTask task,
        int attempt, IReadOnlyList<PuloniaTaskAvailabilityRule> rules, CancellationToken ct)
    {
        var target = state.Request.ExecutionTarget!;
        var dispatcher = _remoteDispatcher ?? throw new InvalidOperationException("当前实例不支持把计划派发到子实例。");
        var requiresGameSession = _definitions[task.TaskType].RequiresGameSession;
        var start = new PuloniaRemoteNodeStart
        {
            ExecutionId = Guid.NewGuid(),
            Kind = PuloniaRemoteNodeKind.Task,
            RequestId = state.RequestId,
            RunId = state.RunId,
            Attempt = attempt,
            TaskAddress = task.TaskAddress,
            TaskJson = PuloniaTaskJson.Write(task),
            SnapshotJson = state.SnapshotJson,
            RequiresGameSession = requiresGameSession,
            ExpectedUid = requiresGameSession ? state.VerifiedUid : null
        };
        var finished = await dispatcher.ExecuteAsync(target, start,
            (completionEvent, token) => ReportCompletionEventAsync(state, task, attempt, rules, completionEvent, token), ct)
            .ConfigureAwait(false);
        switch (finished.ResultKind)
        {
            case PuloniaRemoteNodeResultKind.Completed:
                return finished.ToOutcome();
            case PuloniaRemoteNodeResultKind.AccountFailed:
                // 核验发生在业务动作之前，没有副作用，先撤销本节点的操作意图
                await ClearOperationIntentAsync(state, CancellationToken.None).ConfigureAwait(false);
                throw new PuloniaGenshinAccountException(finished.Message, new InvalidOperationException(finished.Message));
            case PuloniaRemoteNodeResultKind.CancelledByTarget:
                RequestCancellation(state, TaskStopReason.UserRequested, "执行实例本地停止了任务，正在结束本次运行。");
                throw new OperationCanceledException(ct);
            case PuloniaRemoteNodeResultKind.Cancelled:
                throw new InvalidOperationException("执行实例取消了节点：" + finished.Message);
            default:
                throw new InvalidOperationException(finished.Message);
        }
    }

    /// <summary>
    /// 在执行实例的游戏中准备账号并返回完整核验 UID
    /// </summary>
    /// <exception cref="PuloniaGenshinAccountException">执行实例无法准备或核验账号</exception>
    private async Task<string> PrepareRemoteGenshinAccountAsync(RunState state, PuloniaTaskExecutionTarget target,
        CancellationToken ct)
    {
        var dispatcher = _remoteDispatcher
                         ?? throw new PuloniaGenshinAccountException("当前实例不支持把计划派发到子实例。", new InvalidOperationException());
        var start = new PuloniaRemoteNodeStart
        {
            ExecutionId = Guid.NewGuid(),
            Kind = PuloniaRemoteNodeKind.AccountPrepare,
            RequestId = state.RequestId,
            RunId = state.RunId,
            Attempt = 1,
            TaskAddress = state.Snapshot.RootTask.TaskAddress + "/account",
            RequiresGameSession = true,
            ExpectedUid = state.VerifiedUid,
            AccountJson = state.Snapshot.GenshinAccountJson
        };
        PuloniaRemoteNodeFinished finished;
        try
        {
            finished = await dispatcher.ExecuteAsync(target, start, null, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new PuloniaGenshinAccountException("无法在执行实例上准备账号：" + exception.Message, exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 已经取消：按取消处理，确认退出超时等问题由取消路径统一收敛
            throw new OperationCanceledException(exception.Message, exception, ct);
        }

        switch (finished.ResultKind)
        {
            case PuloniaRemoteNodeResultKind.Completed when finished.VerifiedUid is { } uid:
                return uid;
            case PuloniaRemoteNodeResultKind.CancelledByTarget:
                RequestCancellation(state, TaskStopReason.UserRequested, "执行实例本地停止了任务，正在结束本次运行。");
                throw new OperationCanceledException(ct);
            default:
                throw new PuloniaGenshinAccountException(
                    string.IsNullOrWhiteSpace(finished.Message) ? "执行实例没有返回核验 UID。" : finished.Message,
                    new InvalidOperationException(finished.Message));
        }
    }
}
