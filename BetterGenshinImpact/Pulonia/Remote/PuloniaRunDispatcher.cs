using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service.Instance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Pulonia.Remote;

/// <summary>
/// 主实例侧的一步下发客户端与跟踪器：经 <c>pulonia.run.submit</c>、<c>pulonia.run.cancel</c> 向子实例整次下发和停止运行，
/// 并在内存中跟踪本次进程下发的运行（执行实例、计划、状态、消息、连接是否断开），收到 <c>pulonia.run.changed</c> 时更新。
/// 子实例断开时把未结束的运行标记为“连接断开，状态未知”；子实例重连后补发的状态到达即恢复。
/// 运行状态与历史由执行它的子实例保存，这里不落盘；主实例重启后由子实例重连时补发的状态重新得知尚未结束的运行。
/// </summary>
public sealed class PuloniaRunDispatcher
{
    /// <summary>
    /// 延迟取得的通信通道：多实例服务在构造时为收到的请求准备处理器，不能在这里反向注入。
    /// </summary>
    private readonly Func<IPuloniaRunTransport> _transportFactory;

    /// <summary>保护通道的首次取得与事件订阅。</summary>
    private readonly object _transportLock = new();

    /// <summary>已取得的通信通道。</summary>
    private IPuloniaRunTransport? _transport;

    /// <summary>保护 <see cref="_runs"/>；记录不可变，状态变化时整体替换。</summary>
    private readonly object _gate = new();

    /// <summary>跟踪中的下发运行，键为运行 ID。</summary>
    private readonly Dictionary<string, PuloniaDispatchedRun> _runs = new(StringComparer.Ordinal);

    /// <summary>时钟，用于子实例上报前的提交时间。</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>日志。</summary>
    private readonly ILogger<PuloniaRunDispatcher> _logger;

    /// <summary>
    /// 任一下发运行的状态、消息或连接状态变化后触发。可能在管道后台线程触发，界面订阅方需要切回 UI 线程。
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 建立下发客户端；构造时不取得通信通道。
    /// </summary>
    /// <param name="transportFactory">首次使用时取得通信通道的工厂</param>
    /// <param name="timeProvider">时钟；为空时使用系统时钟</param>
    /// <param name="logger">日志；为空时不记录</param>
    public PuloniaRunDispatcher(Func<IPuloniaRunTransport> transportFactory, TimeProvider? timeProvider = null,
        ILogger<PuloniaRunDispatcher>? logger = null)
    {
        _transportFactory = transportFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<PuloniaRunDispatcher>.Instance;
    }

    /// <summary>
    /// 取得通信通道，并在首次取得时订阅子实例断开通知。
    /// </summary>
    private IPuloniaRunTransport Transport
    {
        get
        {
            lock (_transportLock)
            {
                if (_transport is null)
                {
                    _transport = _transportFactory();
                    _transport.ChildInstanceDisconnected += OnChildInstanceDisconnected;
                }
                return _transport;
            }
        }
    }

    /// <summary>
    /// 实例端点的显示名：主实例、桌面分身或“云原神 · 实例名”。
    /// </summary>
    /// <param name="endpoint">实例端点</param>
    /// <returns>界面显示名称</returns>
    public static string Describe(InstanceEndpoint endpoint) => endpoint.InstanceType switch
    {
        BetterGiInstanceType.Primary => "主实例",
        BetterGiInstanceType.ChildSession => "桌面分身",
        BetterGiInstanceType.WebView when !string.IsNullOrWhiteSpace(endpoint.InstanceName) => "云原神 · " + endpoint.InstanceName,
        BetterGiInstanceType.WebView => $"云原神（进程 {endpoint.ProcessId}）",
        _ => $"子实例（进程 {endpoint.ProcessId}）"
    };

    /// <summary>
    /// 实例端点对应的实例键；云原神实例名无效时为 null。
    /// </summary>
    /// <param name="endpoint">实例端点</param>
    /// <returns>实例键或 null</returns>
    public static string? TryGetInstanceKey(InstanceEndpoint endpoint)
    {
        try
        {
            return PuloniaInstanceKeyProvider.GetInstanceKey(endpoint.InstanceType, endpoint.InstanceName);
        }
        catch (Exception ex) when (ex is PuloniaTaskValidationException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// 把一次运行整次下发到子实例：子实例读取共享计划、核对修订号、固定快照并放入它自己的队列后返回运行 ID。
    /// 之后的状态由子实例上报，主实例只显示。
    /// </summary>
    /// <param name="target">在线子实例</param>
    /// <param name="planId">已保存的计划 ID</param>
    /// <param name="planRevision">保存后的计划修订号</param>
    /// <param name="targetTaskId">执行范围；为空表示整个计划</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>子实例生成的运行 ID</returns>
    /// <exception cref="InvalidOperationException">子实例不在线或拒绝（计划已变更、计划无效等，原因在消息中）</exception>
    public async Task<string> SubmitAsync(InstanceEndpoint target, string planId, long planRevision,
        string? targetTaskId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.InstanceType == BetterGiInstanceType.Primary)
            throw new ArgumentException("一步下发只能发给子实例。", nameof(target));
        var instance = TryGetInstanceKey(target) ?? throw new InvalidOperationException("无法识别执行实例。");

        // 1. 子实例校验修订号并入队后才响应；准备失败的原因随失败响应返回
        var response = await Transport.SubmitRunAsync(target, new PuloniaRunSubmitRequest
        {
            PlanId = planId,
            PlanRevision = planRevision,
            TargetTaskId = targetTaskId
        }, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response.RunId))
            throw new InvalidOperationException("子实例没有返回运行 ID。");

        // 2. 开始跟踪；子实例的状态上报可能先于本响应到达，已有记录时保留它
        lock (_gate)
        {
            _runs.TryAdd(response.RunId, new PuloniaDispatchedRun
            {
                RunId = response.RunId,
                PlanId = planId,
                Endpoint = target,
                Instance = instance,
                InstanceName = Describe(target),
                Status = PuloniaTaskRunStatus.Queued,
                Message = "已下发，等待子实例执行。",
                SubmittedAt = _timeProvider.GetUtcNow()
            });
        }
        _logger.LogInformation("已把 Pulonia 计划 {PlanId} 下发到 {Instance}，运行 {RunId}", planId, Describe(target), response.RunId);
        RaiseChanged();
        return response.RunId;
    }

    /// <summary>
    /// 请求子实例停止一个由本实例下发的运行；子实例只发出取消、立即响应，最终状态随后上报。
    /// 运行已结束时直接返回。
    /// </summary>
    /// <param name="runId">运行 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="KeyNotFoundException">不是本实例下发（或得知）的运行</exception>
    /// <exception cref="InvalidOperationException">连接已断开，或子实例拒绝</exception>
    public async Task CancelAsync(string runId, CancellationToken ct = default)
    {
        PuloniaDispatchedRun? run;
        lock (_gate)
            _runs.TryGetValue(runId, out run);
        if (run is null)
            throw new KeyNotFoundException($"运行 {runId} 不是本实例下发的运行。");
        if (!run.IsActive)
            return;
        if (run.IsDisconnected)
            throw new InvalidOperationException($"与“{run.InstanceName}”的连接已断开，无法远程停止，请在该实例的界面停止。");

        await Transport.CancelRunAsync(run.Endpoint, new PuloniaRunCancelRequest { RunId = runId }, ct).ConfigureAwait(false);
        // 先显示“已请求停止”；子实例的结束上报可能已经先到，此时保留上报的状态
        lock (_gate)
        {
            if (_runs.TryGetValue(runId, out var current) && current.Status is PuloniaTaskRunStatus.Queued or PuloniaTaskRunStatus.Running)
                _runs[runId] = current with
                {
                    Status = PuloniaTaskRunStatus.Cancelling,
                    Message = "已请求停止，等待子实例结束运行。"
                };
        }
        RaiseChanged();
    }

    /// <summary>
    /// 按提交时间倒序列出跟踪中的下发运行。
    /// </summary>
    /// <returns>运行状态的快照</returns>
    public IReadOnlyList<PuloniaDispatchedRun> ListRuns()
    {
        lock (_gate)
            return _runs.Values.OrderByDescending(run => run.SubmittedAt).ToArray();
    }

    /// <summary>
    /// 实例通信层收到子实例的 <c>pulonia.run.changed</c> 时调用：更新状态并清除“连接断开”。
    /// 主实例重启后由子实例补发得知的运行同样开始跟踪；同一运行 ID 来自其他实例的上报被忽略。
    /// </summary>
    /// <param name="source">发送上报的子实例端点</param>
    /// <param name="report">运行状态</param>
    /// <exception cref="ArgumentException">上报缺少运行 ID 或计划 ID</exception>
    public void ReceiveChanged(InstanceEndpoint source, PuloniaRunChangedReport report)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(report);
        if (string.IsNullOrWhiteSpace(report.RunId) || string.IsNullOrWhiteSpace(report.PlanId))
            throw new ArgumentException("Pulonia 运行状态上报缺少运行 ID 或计划 ID。");
        var instance = TryGetInstanceKey(source);
        if (instance is null)
            return;
        // 确保已订阅断开通知：主实例重启后可能还没有下发过就先收到补发
        _ = Transport;

        lock (_gate)
        {
            _runs.TryGetValue(report.RunId, out var existing);
            // 运行 ID 只属于执行它的实例；来自其他实例的同名上报不可能是这个运行
            if (existing is not null && existing.Instance != instance)
                return;
            _runs[report.RunId] = new PuloniaDispatchedRun
            {
                RunId = report.RunId,
                PlanId = report.PlanId,
                PlanName = report.PlanName,
                Endpoint = source,
                Instance = instance,
                InstanceName = Describe(source),
                Status = report.Status,
                Message = report.Message,
                SubmittedAt = report.SubmittedAt,
                StartedAt = report.StartedAt,
                FinishedAt = report.FinishedAt,
                IsDisconnected = false
            };
        }
        RaiseChanged();
    }

    /// <summary>
    /// 子实例断开：它上面未结束的运行改为“连接断开，状态未知”；运行本身不受影响，仍在子实例继续。
    /// </summary>
    /// <param name="endpoint">断开的子实例端点</param>
    private void OnChildInstanceDisconnected(InstanceEndpoint endpoint)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var run in _runs.Values.ToArray())
            {
                if (run.IsDisconnected || !run.IsActive || run.Endpoint.ProcessId != endpoint.ProcessId
                    || run.Endpoint.InstanceType != endpoint.InstanceType)
                    continue;
                _runs[run.RunId] = run with { IsDisconnected = true };
                changed = true;
            }
        }
        if (changed)
            RaiseChanged();
    }

    /// <summary>
    /// 通知订阅方；订阅方异常不影响实例通信与跟踪。
    /// </summary>
    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "通知 Pulonia 下发运行变化失败");
        }
    }
}
