using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Pulonia.Remote;

/// <summary>
/// 子实例侧的一步下发宿主：接受主实例的 <c>pulonia.run.submit</c> 与 <c>pulonia.run.cancel</c>，
/// 并把主实例下发到本实例的运行状态尽力回报主实例（<c>pulonia.run.changed</c>），只用于主实例显示。
/// 回报失败不影响运行；主实例断开或崩溃不取消已接受的运行。重新连上主实例后补发一次所有尚未结束的下发运行，
/// 以及本进程中最近一次回报没有送达的下发运行（例如断开期间结束的运行）。主实例上不启用。
/// </summary>
public sealed class PuloniaRunDispatchHost : IHostedService
{
    /// <summary>本实例的 Pulonia 协调器。</summary>
    private readonly IPuloniaTaskService _service;

    /// <summary>延迟取得的通信通道。</summary>
    private readonly Func<IPuloniaRunTransport> _transportFactory;

    /// <summary>本实例的实例键；只回报执行实例为本实例的运行。</summary>
    private readonly string _instanceKey;

    /// <summary>日志。</summary>
    private readonly ILogger<PuloniaRunDispatchHost> _logger;

    /// <summary>保护待发送集合、未送达集合与发送循环状态。</summary>
    private readonly object _gate = new();

    /// <summary>等待回报的运行 ID；同一运行多次变化合并为一次，发送时读取最新状态。</summary>
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);

    /// <summary>最近一次回报没有送达的运行 ID，重新连上主实例后补发。</summary>
    private readonly HashSet<string> _undelivered = new(StringComparer.Ordinal);

    /// <summary>发送循环是否在运行；同一时刻只有一个循环，按顺序发送。</summary>
    private bool _pumping;

    /// <summary>
    /// 是否需要补发：根连接登记后置位，由发送循环在下一轮计算补发集合。放在循环内计算，
    /// 连接恢复前刚失败的回报已经记入未送达集合，不会漏掉。
    /// </summary>
    private bool _resendRequested;

    /// <summary>已订阅的通信通道；未启动或已停止时为空。</summary>
    private IPuloniaRunTransport? _transport;

    /// <summary>
    /// 建立宿主；构造时不订阅事件，由 <see cref="Start"/> 开始回报。
    /// </summary>
    /// <param name="service">本实例的 Pulonia 协调器</param>
    /// <param name="instanceKeyProvider">本实例的实例键</param>
    /// <param name="transportFactory">首次使用时取得通信通道的工厂</param>
    /// <param name="logger">日志；为空时不记录</param>
    public PuloniaRunDispatchHost(IPuloniaTaskService service, IPuloniaInstanceKeyProvider instanceKeyProvider,
        Func<IPuloniaRunTransport> transportFactory, ILogger<PuloniaRunDispatchHost>? logger = null)
    {
        _service = service;
        _instanceKey = instanceKeyProvider.CurrentInstanceKey;
        _transportFactory = transportFactory;
        _logger = logger ?? NullLogger<PuloniaRunDispatchHost>.Instance;
    }

    /// <summary>是否接受下发：只有子实例接受，主实例不接受也不回报。</summary>
    private bool IsEnabled => _instanceKey != PuloniaInstanceKey.Primary;

    /// <summary>
    /// 开始回报：订阅本实例的运行变化与根连接登记，并立即安排一次补发（重启后从运行状态恢复的下发运行，
    /// 可能在订阅之前就已连上主实例；尚未连接时补发失败，记为未送达，连接登记后再补发）。
    /// 重复调用无效果；主实例上不做任何事。
    /// </summary>
    public void Start()
    {
        if (!IsEnabled)
            return;
        lock (_gate)
        {
            if (_transport is not null)
                return;
            _transport = _transportFactory();
        }
        _service.RunChanged += OnRunChanged;
        _transport.RootConnected += OnRootConnected;
        OnRootConnected();
    }

    /// <summary>
    /// 停止回报，解除事件订阅；已接受的运行不受影响。
    /// </summary>
    public void Stop()
    {
        IPuloniaRunTransport? transport;
        lock (_gate)
        {
            transport = _transport;
            _transport = null;
        }
        if (transport is null)
            return;
        _service.RunChanged -= OnRunChanged;
        transport.RootConnected -= OnRootConnected;
    }

    /// <inheritdoc />
    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        Stop();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 接受主实例的一次下发：读取共享计划，修订号一致时固定快照、放入本实例队列并返回运行 ID。
    /// 计划已变更、计划无效等准备失败的原因转为可回报给主实例的错误。
    /// </summary>
    /// <param name="request">下发内容</param>
    /// <param name="ct">取消令牌（连接关闭时取消）</param>
    /// <returns>新运行 ID</returns>
    /// <exception cref="InvalidOperationException">本实例是主实例，或准备失败（原因在消息中）</exception>
    public async Task<PuloniaRunSubmitResponse> SubmitAsync(PuloniaRunSubmitRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsEnabled)
            throw new InvalidOperationException("主实例不接受一步下发。");
        try
        {
            var runId = await _service.EnqueueDispatchedAsync(request.PlanId, request.PlanRevision, request.TargetTaskId, ct)
                .ConfigureAwait(false);
            _logger.LogInformation("已接受主实例下发的 Pulonia 运行 {RunId}（计划 {PlanId}）", runId, request.PlanId);
            return new PuloniaRunSubmitResponse { RunId = runId };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 写入本实例日志；只把原因文字回给主实例
            _logger.LogWarning(ex, "未能接受主实例下发的 Pulonia 计划 {PlanId}", request.PlanId);
            throw new InvalidOperationException(ex.Message);
        }
    }

    /// <summary>
    /// 按主实例请求停止一个运行：只允许停止主实例下发的运行；只发出取消，不等待运行结束。
    /// 运行已结束时直接返回。
    /// </summary>
    /// <param name="request">要停止的运行</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="InvalidOperationException">运行不存在、不是主实例下发的运行，或本实例是主实例</exception>
    public async Task CancelAsync(PuloniaRunCancelRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsEnabled)
            throw new InvalidOperationException("主实例不接受远程停止。");
        // 1. 只能停止主实例下发到本实例的运行
        PuloniaTaskRunView run;
        try
        {
            run = await _service.GetRunAsync(request.RunId, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            throw new InvalidOperationException($"运行 {request.RunId} 不存在。");
        }
        if (!run.IsDispatched || run.Instance != _instanceKey)
            throw new InvalidOperationException("只能停止主实例下发的运行，本实例自己提交的运行请在本实例界面停止。");
        // 2. 只发出取消；执行器退出后的最终状态经状态上报送达主实例
        try
        {
            await _service.RequestCancelAsync(request.RunId, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            throw new InvalidOperationException($"运行 {request.RunId} 不存在。");
        }
        _logger.LogInformation("主实例请求停止 Pulonia 运行 {RunId}", request.RunId);
    }

    /// <summary>运行状态变化：安排一次回报，是否属于下发运行在发送时判断。</summary>
    private void OnRunChanged(object? sender, PuloniaTaskRunChangedEventArgs e) => Schedule([e.RunId]);

    /// <summary>根连接登记成功：请求发送循环补发尚未结束和没有送达的下发运行。</summary>
    private void OnRootConnected()
    {
        lock (_gate)
        {
            if (_transport is null)
                return;
            _resendRequested = true;
            if (_pumping)
                return;
            _pumping = true;
        }
        _ = Task.Run(PumpAsync);
    }

    /// <summary>
    /// 需要补发的运行：本实例所有主实例下发、尚未结束的运行，以及最近一次回报没有送达的下发运行。
    /// </summary>
    /// <returns>运行 ID；运行状态读取失败（如初始化失败）时为空</returns>
    private async Task<IReadOnlyList<string>> ListResendRunIdsAsync()
    {
        try
        {
            var runs = await _service.ListRunsAsync().ConfigureAwait(false);
            string[] undelivered;
            lock (_gate)
                undelivered = _undelivered.ToArray();
            return runs.Where(run => run.IsDispatched && run.Instance == _instanceKey
                                     && (PuloniaTaskRunClassifier.IsActive(run.Status) || undelivered.Contains(run.RunId)))
                .Select(run => run.RunId).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取需要补发的 Pulonia 下发运行失败");
            return [];
        }
    }

    /// <summary>
    /// 把运行加入待发送集合；没有发送循环时启动一个。
    /// </summary>
    /// <param name="runIds">要回报的运行 ID</param>
    private void Schedule(IEnumerable<string> runIds)
    {
        lock (_gate)
        {
            if (_transport is null)
                return;
            foreach (var runId in runIds)
                _pending.Add(runId);
            if (_pumping || _pending.Count == 0)
                return;
            _pumping = true;
        }
        _ = Task.Run(PumpAsync);
    }

    /// <summary>
    /// 唯一的发送循环：逐个读取最新状态并发送，直到没有待发送的运行和补发请求；按顺序发送，主实例收到的状态不会倒退。
    /// </summary>
    private async Task PumpAsync()
    {
        while (true)
        {
            string[] batch;
            bool resend;
            IPuloniaRunTransport? transport;
            lock (_gate)
            {
                transport = _transport;
                resend = _resendRequested;
                _resendRequested = false;
                if (transport is null || (!resend && _pending.Count == 0))
                {
                    _pending.Clear();
                    _pumping = false;
                    return;
                }
                batch = _pending.ToArray();
                _pending.Clear();
            }
            // 补发集合在循环内计算：此前失败的回报都已记入未送达集合
            if (resend)
                batch = batch.Union(await ListResendRunIdsAsync().ConfigureAwait(false), StringComparer.Ordinal).ToArray();
            foreach (var runId in batch)
                await ReportAsync(transport, runId).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 回报一个运行的最新状态；只回报主实例下发到本实例的运行。失败时记为未送达，重新连接后补发。
    /// </summary>
    /// <param name="transport">通信通道</param>
    /// <param name="runId">运行 ID</param>
    private async Task ReportAsync(IPuloniaRunTransport transport, string runId)
    {
        PuloniaTaskRunView run;
        try
        {
            run = await _service.GetRunAsync(runId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 运行已不存在或状态读取失败：没有可回报的内容
            _logger.LogDebug(ex, "读取 Pulonia 运行 {RunId} 失败，跳过回报", runId);
            return;
        }
        // 本实例自己从界面、定时或命令行提交的运行不经过实例通信，也不回报
        if (!run.IsDispatched || run.Instance != _instanceKey)
            return;
        try
        {
            await transport.ReportRunChangedAsync(PuloniaRunChangedReport.FromView(run), CancellationToken.None)
                .ConfigureAwait(false);
            lock (_gate)
                _undelivered.Remove(runId);
        }
        catch (Exception ex)
        {
            // 尽力回报：未连接、超时或主实例拒绝都不影响运行
            lock (_gate)
                _undelivered.Add(runId);
            _logger.LogDebug(ex, "向主实例回报 Pulonia 运行 {RunId} 的状态失败，重新连接后补发", runId);
        }
    }
}
