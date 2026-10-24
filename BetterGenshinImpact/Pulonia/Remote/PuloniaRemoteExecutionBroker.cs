using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.Pulonia.Remote;

/// <summary>
/// 主实例侧的远程节点派发：发送启动请求，按执行标识等待子实例的结束回报，并把子实例转发的完成事件交给协调器落盘。
/// 子实例断开时让等待中的节点立即失败
/// </summary>
public sealed class PuloniaRemoteExecutionBroker : IPuloniaRemoteNodeDispatcher
{
    /// <summary>
    /// 取消后等待子实例确认执行器退出的上限
    /// </summary>
    private static readonly TimeSpan StopConfirmTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 完成事件落盘的时限，与协调器关键写入保持一致
    /// </summary>
    private static readonly TimeSpan ReportPersistTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 启动负载上限，低于实例通信的 1 MiB 帧限制，给信封字段留出余量
    /// </summary>
    private const int MaxStartPayloadBytes = 900 * 1024;

    /// <summary>
    /// 延迟取得的通信通道：多实例服务也依赖 Pulonia 服务，构造时不能直接注入
    /// </summary>
    private readonly Func<IPuloniaRemoteTransport> _transportFactory;

    /// <summary>
    /// 保护通道的首次创建与事件订阅
    /// </summary>
    private readonly object _transportLock = new();

    /// <summary>
    /// 已取得的通信通道
    /// </summary>
    private IPuloniaRemoteTransport? _transport;

    /// <summary>
    /// 等待结束回报的远程执行，键为执行标识
    /// </summary>
    private readonly ConcurrentDictionary<Guid, PendingExecution> _pending = new();

    /// <summary>
    /// 创建派发代理
    /// </summary>
    /// <param name="transportFactory">首次派发时取得通信通道的工厂</param>
    public PuloniaRemoteExecutionBroker(Func<IPuloniaRemoteTransport> transportFactory)
    {
        _transportFactory = transportFactory;
    }

    /// <summary>
    /// 取得通信通道，并在首次取得时订阅子实例断开通知
    /// </summary>
    private IPuloniaRemoteTransport Transport
    {
        get
        {
            lock (_transportLock)
            {
                if (_transport is null)
                {
                    _transport = _transportFactory();
                    _transport.RemoteInstanceDisconnected += OnRemoteInstanceDisconnected;
                }

                return _transport;
            }
        }
    }

    /// <inheritdoc />
    public bool IsAvailable(PuloniaTaskExecutionTarget target) => Transport.ResolveTargetProcessId(target) is not null;

    /// <inheritdoc />
    public async Task<PuloniaRemoteNodeFinished> ExecuteAsync(PuloniaTaskExecutionTarget target,
        PuloniaRemoteNodeStart start, Func<PuloniaTaskCompletionEvent, CancellationToken, Task>? reporter,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsurePayloadSize(start);
        var transport = Transport;
        var processId = transport.ResolveTargetProcessId(target)
                        ?? throw new InvalidOperationException($"执行实例“{target.DisplayName}”当前不在线。");
        var pending = new PendingExecution(processId, reporter);
        // 先登记再发送：子实例可能在启动确认送达之前就回报结束
        if (!_pending.TryAdd(start.ExecutionId, pending))
            throw new InvalidOperationException("远程节点执行标识重复。");
        try
        {
            await SendStartAsync(transport, processId, start).ConfigureAwait(false);
            try
            {
                return await pending.Completion.Task.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 与本地执行一致：取消后要等执行器真正退出，才算本节点结束
                await TryCancelAsync(transport, processId, start.ExecutionId).ConfigureAwait(false);
                await pending.Completion.Task.WaitAsync(StopConfirmTimeout).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _pending.TryRemove(start.ExecutionId, out _);
        }
    }

    /// <summary>
    /// 子实例转发完成事件：交给发起该节点的协调器回调落盘，落盘失败时抛出，子实例的执行器随之失败
    /// </summary>
    /// <param name="sourceProcessId">发送方子实例进程 ID</param>
    /// <param name="report">完成事件</param>
    /// <exception cref="InvalidOperationException">节点不存在、已结束或不接受完成事件</exception>
    public async Task ReceiveReportAsync(int sourceProcessId, PuloniaRemoteNodeReport report)
    {
        if (!_pending.TryGetValue(report.ExecutionId, out var pending) || pending.ProcessId != sourceProcessId)
            throw new InvalidOperationException("远程节点不存在或已经结束，完成事件未记录。");
        if (pending.Reporter is null)
            throw new InvalidOperationException("该远程操作不接受完成事件。");
        var completionEvent = PuloniaTaskJson.Read<PuloniaTaskCompletionEvent>(report.EventJson);
        using var timeout = new CancellationTokenSource(ReportPersistTimeout);
        await pending.Reporter(completionEvent, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 子实例回报节点结束；未知或已结束的执行标识直接忽略，重复回报不影响首次结果
    /// </summary>
    /// <param name="sourceProcessId">发送方子实例进程 ID</param>
    /// <param name="finished">结束回报</param>
    public void ReceiveFinished(int sourceProcessId, PuloniaRemoteNodeFinished finished)
    {
        if (_pending.TryGetValue(finished.ExecutionId, out var pending) && pending.ProcessId == sourceProcessId)
            pending.Completion.TrySetResult(finished);
    }

    /// <summary>
    /// 子实例断开时让发往它的节点立即失败，协调器按节点异常处理（存在操作意图时转为待核验）
    /// </summary>
    /// <param name="processId">断开的子实例进程 ID</param>
    private void OnRemoteInstanceDisconnected(int processId)
    {
        foreach (var pending in _pending.Values.Where(item => item.ProcessId == processId))
            pending.Completion.TrySetException(new IOException("执行实例已断开连接，节点结果未知。"));
    }

    /// <summary>
    /// 发送启动请求。不跟随节点令牌中途放弃，避免子实例已开始执行而主实例不再等待；
    /// 确认超时时尽力补发取消
    /// </summary>
    private static async Task SendStartAsync(IPuloniaRemoteTransport transport, int processId, PuloniaRemoteNodeStart start)
    {
        try
        {
            await transport.StartRemoteNodeAsync(processId, start, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await TryCancelAsync(transport, processId, start.ExecutionId).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 尽力发送取消请求；子实例已断开时由断开通知结束等待
    /// </summary>
    private static async Task TryCancelAsync(IPuloniaRemoteTransport transport, int processId, Guid executionId)
    {
        try
        {
            await transport.CancelRemoteNodeAsync(processId, executionId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            // 连接问题由断开通知或确认时限兜底
        }
    }

    /// <summary>
    /// 检查启动负载不超过实例通信的帧上限
    /// </summary>
    /// <exception cref="InvalidOperationException">负载过大</exception>
    private static void EnsurePayloadSize(PuloniaRemoteNodeStart start)
    {
        var bytes = Encoding.UTF8.GetByteCount(start.SnapshotJson ?? string.Empty)
                    + Encoding.UTF8.GetByteCount(start.TaskJson ?? string.Empty)
                    + Encoding.UTF8.GetByteCount(start.AccountJson ?? string.Empty);
        if (bytes > MaxStartPayloadBytes)
            throw new InvalidOperationException($"运行快照约 {bytes / 1024} KB，超过跨实例传输上限，请拆分计划后再派发到子实例。");
    }

    /// <summary>
    /// 一次等待中的远程执行
    /// </summary>
    /// <param name="processId">执行节点的子实例进程 ID</param>
    /// <param name="reporter">完成事件落盘回调</param>
    private sealed class PendingExecution(int processId, Func<PuloniaTaskCompletionEvent, CancellationToken, Task>? reporter)
    {
        /// <summary>
        /// 执行节点的子实例进程 ID，回报必须来自同一进程
        /// </summary>
        public int ProcessId { get; } = processId;

        /// <summary>
        /// 完成事件落盘回调
        /// </summary>
        public Func<PuloniaTaskCompletionEvent, CancellationToken, Task>? Reporter { get; } = reporter;

        /// <summary>
        /// 结束回报；续体不在回报线程上同步执行
        /// </summary>
        public TaskCompletionSource<PuloniaRemoteNodeFinished> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
