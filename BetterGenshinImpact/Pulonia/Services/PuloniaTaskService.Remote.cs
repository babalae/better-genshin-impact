using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Service;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 跨实例派发：所有运行共用一个串行队列，执行实例只决定节点在哪个游戏中执行，不同实例之间不并行；
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
    /// 执行实例的显示名
    /// </summary>
    private static string DescribeTarget(PuloniaTaskExecutionTarget? target)
        => target is null ? "主实例" : string.IsNullOrWhiteSpace(target.DisplayName) ? target.LaneKey : target.DisplayName;

    /// <summary>
    /// 把一个叶子节点交给执行实例执行，并把结束回报转换为本地执行器的等价结果或异常：
    /// 账号失败转为待处理；执行实例本地停止时取消整次运行，不进入重试；其余失败按节点异常处理。
    /// </summary>
    /// <remarks>
    /// <see cref="PuloniaRemoteNodeResultKind.Cancelled"/> 与 <see cref="PuloniaRemoteNodeResultKind.CancelledByTarget"/> 语义不同、保留两者：
    /// 前者是子实例按本协调器的取消请求（或与主实例断开）退出，此时本协调器的节点令牌已经取消，派发代理等待子实例确认退出后
    /// 直接抛出取消，不会把回报交到这里；后者是子实例本地停止，要停止整次运行。
    /// </remarks>
    private async Task<PuloniaTaskOutcome> ExecuteRemoteNodeAsync(RunState state, PuloniaTaskPreparedTask task,
        int attempt, CancellationToken ct)
    {
        var target = state.Request.ExecutionTarget!;
        var dispatcher = _remoteDispatcher ?? throw new InvalidOperationException("当前实例不支持把计划派发到子实例。");
        var requiresGameSession = _definitions[task.TaskType].RequiresGameSession;
        var start = new PuloniaRemoteNodeStart
        {
            ExecutionId = Guid.NewGuid(),
            Kind = PuloniaRemoteNodeKind.Task,
            RunId = state.RunId,
            Attempt = attempt,
            TaskAddress = task.TaskAddress,
            TaskJson = PuloniaTaskJson.Write(task),
            SnapshotJson = state.SnapshotJson,
            RequiresGameSession = requiresGameSession,
            ExpectedUid = requiresGameSession ? state.VerifiedUid : null
        };
        var finished = await dispatcher.ExecuteAsync(target, start, ct).ConfigureAwait(false);
        switch (finished.ResultKind)
        {
            case PuloniaRemoteNodeResultKind.Completed:
                return finished.ToOutcome();
            case PuloniaRemoteNodeResultKind.AccountFailed:
                // 核验发生在业务动作之前，没有副作用，整次运行转为待处理
                throw new PuloniaGenshinAccountException(finished.Message, new InvalidOperationException(finished.Message));
            case PuloniaRemoteNodeResultKind.CancelledByTarget:
                RequestCancellation(state, TaskStopReason.UserRequested, "执行实例本地停止了任务，正在结束本次运行。");
                throw new OperationCanceledException(ct);
            case PuloniaRemoteNodeResultKind.Cancelled:
                // 只有本协调器请求取消时子实例才回报 Cancelled，正常不会走到这里；按节点失败处理，不扩大为整次运行取消
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
            finished = await dispatcher.ExecuteAsync(target, start, ct).ConfigureAwait(false);
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
