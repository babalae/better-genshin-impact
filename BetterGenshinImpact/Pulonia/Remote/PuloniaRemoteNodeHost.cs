using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Pulonia.Remote;

/// <summary>
/// 子实例侧的远程节点执行：用本进程的执行器和游戏环境运行主实例派发的节点，并把完成事件与结束结果回报主实例。
/// 不读写 Pulonia 状态文件；与主实例断开、本地停止时取消正在执行的节点
/// </summary>
public sealed class PuloniaRemoteNodeHost
{
    /// <summary>
    /// 结束回报的重试次数，期间连接仍在但请求超时时使用
    /// </summary>
    private const int FinishRetryCount = 3;

    /// <summary>
    /// 延迟取得的通信通道
    /// </summary>
    private readonly Func<IPuloniaRemoteTransport> _transportFactory;

    /// <summary>
    /// 按任务类型查找的本进程执行器
    /// </summary>
    private readonly IReadOnlyDictionary<string, IPuloniaTaskExecutor> _executors;

    /// <summary>
    /// 游戏节点的任务锁、运行环境与任务模式
    /// </summary>
    private readonly PuloniaGameTaskCoordinator _gameTaskCoordinator;

    /// <summary>
    /// 账号准备与核验；没有注册时账号相关节点失败
    /// </summary>
    private readonly IPuloniaGenshinAccountSwitcher? _accountSwitcher;

    /// <summary>
    /// 本地停止信号（停止热键、截图器停止、应用关闭）
    /// </summary>
    private readonly TaskStopService _taskStopService;

    /// <summary>
    /// 日志
    /// </summary>
    private readonly ILogger<PuloniaRemoteNodeHost> _logger;

    /// <summary>
    /// 保护通道的首次创建与事件订阅
    /// </summary>
    private readonly object _transportLock = new();

    /// <summary>
    /// 已取得的通信通道
    /// </summary>
    private IPuloniaRemoteTransport? _transport;

    /// <summary>
    /// 正在执行的节点，键为执行标识
    /// </summary>
    private readonly ConcurrentDictionary<Guid, RunningNode> _running = new();

    /// <summary>
    /// 创建远程节点执行宿主
    /// </summary>
    /// <param name="transportFactory">首次执行时取得通信通道的工厂</param>
    /// <param name="executors">本进程注册的执行器</param>
    /// <param name="gameTaskCoordinator">游戏节点协调器</param>
    /// <param name="taskStopService">本地停止信号</param>
    /// <param name="logger">日志</param>
    /// <param name="accountSwitcher">账号切换与核验</param>
    /// <exception cref="InvalidOperationException">同一任务类型重复注册执行器</exception>
    public PuloniaRemoteNodeHost(Func<IPuloniaRemoteTransport> transportFactory, IEnumerable<IPuloniaTaskExecutor> executors,
        PuloniaGameTaskCoordinator gameTaskCoordinator, TaskStopService taskStopService,
        ILogger<PuloniaRemoteNodeHost> logger, IPuloniaGenshinAccountSwitcher? accountSwitcher = null)
    {
        _transportFactory = transportFactory;
        _gameTaskCoordinator = gameTaskCoordinator;
        _taskStopService = taskStopService;
        _logger = logger;
        _accountSwitcher = accountSwitcher;
        var map = new Dictionary<string, IPuloniaTaskExecutor>(StringComparer.Ordinal);
        foreach (var executor in executors)
        foreach (var definition in executor.Definitions)
        {
            if (!map.TryAdd(definition.TaskType, executor))
                throw new InvalidOperationException($"Pulonia 任务类型 {definition.TaskType} 重复注册执行器。");
        }
        _executors = map;
    }

    /// <summary>
    /// 取得通信通道，并在首次取得时订阅与主实例断开的通知
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
                    _transport.RootConnectionLost += CancelAll;
                }

                return _transport;
            }
        }
    }

    /// <summary>
    /// 接受一个节点并在后台执行；校验失败时抛出，主实例收到失败响应
    /// </summary>
    /// <param name="start">节点启动信息</param>
    /// <exception cref="ArgumentException">启动信息缺失必要字段</exception>
    /// <exception cref="InvalidOperationException">同一执行标识已在执行</exception>
    public void Start(PuloniaRemoteNodeStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (start.ExecutionId == Guid.Empty)
            throw new ArgumentException("远程节点执行标识不能为空。");
        if (start.Kind == PuloniaRemoteNodeKind.Task && (string.IsNullOrEmpty(start.TaskJson) || string.IsNullOrEmpty(start.SnapshotJson)))
            throw new ArgumentException("远程节点缺少任务或快照。");
        if (start.Kind == PuloniaRemoteNodeKind.AccountPrepare && string.IsNullOrEmpty(start.AccountJson))
            throw new ArgumentException("远程账号准备缺少账号资料。");

        _ = Transport;
        var node = new RunningNode(start);
        if (!_running.TryAdd(start.ExecutionId, node))
        {
            node.Dispose();
            throw new InvalidOperationException("该远程节点已经在执行。");
        }

        _logger.LogInformation("开始执行主实例派发的 Pulonia 节点：{Address}（第 {Attempt} 次）", start.TaskAddress, start.Attempt);
        _ = Task.Run(() => RunAsync(node));
    }

    /// <summary>
    /// 按主实例请求取消节点；节点已结束时忽略
    /// </summary>
    /// <param name="executionId">远程执行标识</param>
    public void Cancel(Guid executionId)
    {
        if (_running.TryGetValue(executionId, out var node))
            node.Cancel(stoppedLocally: false);
    }

    /// <summary>
    /// 与主实例断开：结果已经无人记录，取消全部节点
    /// </summary>
    private void CancelAll()
    {
        foreach (var node in _running.Values)
            node.Cancel(stoppedLocally: false);
    }

    /// <summary>
    /// 执行一个节点并回报结果；任何异常都转换为结束回报，不会让后台任务以失败结束
    /// </summary>
    private async Task RunAsync(RunningNode node)
    {
        var start = node.Start;
        PuloniaRemoteNodeFinished finished;
        try
        {
            // 本地停止热键、截图器停止或应用关闭：结束本节点，并告知主实例整次运行随之取消
            using var stopRegistration = _taskStopService.Register(_ => node.Cancel(stoppedLocally: true));
            finished = start.Kind == PuloniaRemoteNodeKind.AccountPrepare
                ? await PrepareAccountAsync(start, node.Token).ConfigureAwait(false)
                : await ExecuteTaskAsync(start, node.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (node.Token.IsCancellationRequested)
        {
            finished = new PuloniaRemoteNodeFinished
            {
                ExecutionId = start.ExecutionId,
                ResultKind = node.StoppedLocally
                    ? PuloniaRemoteNodeResultKind.CancelledByTarget
                    : PuloniaRemoteNodeResultKind.Cancelled,
                Message = node.StoppedLocally ? "执行实例本地停止了节点。" : "节点已按主实例请求取消。"
            };
        }
        catch (RemoteAccountException exception)
        {
            finished = new PuloniaRemoteNodeFinished
            {
                ExecutionId = start.ExecutionId,
                ResultKind = PuloniaRemoteNodeResultKind.AccountFailed,
                Message = exception.Message
            };
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "执行主实例派发的 Pulonia 节点失败：{Address}", start.TaskAddress);
            finished = new PuloniaRemoteNodeFinished
            {
                ExecutionId = start.ExecutionId,
                ResultKind = PuloniaRemoteNodeResultKind.Failed,
                Message = exception.Message
            };
        }
        finally
        {
            _running.TryRemove(start.ExecutionId, out _);
            node.Dispose();
        }

        await SendFinishedAsync(finished).ConfigureAwait(false);
    }

    /// <summary>
    /// 用本进程执行器执行快照节点；游戏节点先取得输入并核验账号，与主实例本地执行的顺序一致
    /// </summary>
    private async Task<PuloniaRemoteNodeFinished> ExecuteTaskAsync(PuloniaRemoteNodeStart start, CancellationToken ct)
    {
        var task = PuloniaTaskJson.Read<PuloniaTaskPreparedTask>(start.TaskJson!);
        var snapshot = PuloniaTaskJson.ReadSnapshot(start.SnapshotJson!);
        if (!_executors.TryGetValue(task.TaskType, out var executor))
            throw new InvalidOperationException($"执行实例没有注册 {task.TaskType} 执行器。");

        using var lease = start.RequiresGameSession
            ? await _gameTaskCoordinator.AcquireAsync(ct).ConfigureAwait(false)
            : null;
        if (lease is not null && start.ExpectedUid is { } expectedUid && _accountSwitcher is not null)
        {
            try
            {
                await _accountSwitcher.VerifyAsync(lease.Runtime, expectedUid, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new RemoteAccountException(exception.Message, exception);
            }
        }

        var context = new PuloniaTaskExecutionContext(start.RequestId, start.RunId, snapshot, start.Attempt,
            task.TaskAddress, lease?.Runtime, (completionEvent, token) => ReportAsync(start.ExecutionId, completionEvent, token));
        PuloniaTaskOutcome outcome;
        // 持有任务锁期间标记会话，执行器内部不能再通过 TaskRunner 启动任务
        using (lease is not null ? GameTaskSessionScope.Enter() : null)
        {
            outcome = await executor.ExecuteAsync(task, context, ct).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
        return PuloniaRemoteNodeFinished.FromOutcome(start.ExecutionId, outcome);
    }

    /// <summary>
    /// 在本进程的游戏中准备账号并返回完整核验 UID；续跑时只核验历史 UID
    /// </summary>
    private async Task<PuloniaRemoteNodeFinished> PrepareAccountAsync(PuloniaRemoteNodeStart start, CancellationToken ct)
    {
        if (_accountSwitcher is null)
            throw new RemoteAccountException("执行实例没有账号切换能力。", new InvalidOperationException());
        var account = PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(start.AccountJson!);
        PuloniaGameTaskLease lease;
        try
        {
            lease = await _gameTaskCoordinator.AcquireAsync(ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RemoteAccountException("无法准备账号的游戏环境：" + exception.Message, exception);
        }

        using (lease)
        {
            string uid;
            try
            {
                if (start.ExpectedUid is { } expected)
                {
                    if (!account.IsDefault)
                        await _accountSwitcher.PrepareAsync(lease.Runtime, account, ct).ConfigureAwait(false);
                    await _accountSwitcher.VerifyAsync(lease.Runtime, expected, ct).ConfigureAwait(false);
                    uid = expected;
                }
                else
                {
                    uid = await _accountSwitcher.PrepareAsync(lease.Runtime, account, ct).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw new RemoteAccountException(exception.Message, exception);
            }

            return new PuloniaRemoteNodeFinished
            {
                ExecutionId = start.ExecutionId,
                ResultKind = PuloniaRemoteNodeResultKind.Completed,
                Message = "账号已核验，实际 UID：" + uid,
                VerifiedUid = uid
            };
        }
    }

    /// <summary>
    /// 把执行器的完成事件转发给主实例；主实例落盘成功后才返回，失败时执行器随之失败
    /// </summary>
    private Task ReportAsync(Guid executionId, PuloniaTaskCompletionEvent completionEvent, CancellationToken ct)
    {
        return Transport.ReportRemoteNodeAsync(new PuloniaRemoteNodeReport
        {
            ExecutionId = executionId,
            EventJson = PuloniaTaskJson.Write(completionEvent)
        }, ct);
    }

    /// <summary>
    /// 回报节点结束；请求超时时重试，连接已断开时放弃（主实例已按断开处理该节点）
    /// </summary>
    private async Task SendFinishedAsync(PuloniaRemoteNodeFinished finished)
    {
        for (var attempt = 1; attempt <= FinishRetryCount; attempt++)
        {
            try
            {
                await Transport.FinishRemoteNodeAsync(finished, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException) when (attempt < FinishRetryCount)
            {
                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
            {
                _logger.LogWarning(exception, "向主实例回报 Pulonia 节点结束失败：{ExecutionId}", finished.ExecutionId);
                return;
            }
        }
    }

    /// <summary>
    /// 一个正在执行的远程节点
    /// </summary>
    /// <param name="start">节点启动信息</param>
    private sealed class RunningNode(PuloniaRemoteNodeStart start) : IDisposable
    {
        /// <summary>
        /// 节点取消令牌源
        /// </summary>
        private readonly CancellationTokenSource _cancellation = new();

        /// <summary>
        /// 节点启动信息
        /// </summary>
        public PuloniaRemoteNodeStart Start { get; } = start;

        /// <summary>
        /// 节点取消令牌
        /// </summary>
        public CancellationToken Token => _cancellation.Token;

        /// <summary>
        /// 是否由本实例的停止信号取消（而不是主实例请求）
        /// </summary>
        public bool StoppedLocally { get; private set; }

        /// <summary>
        /// 取消节点；首次取消的来源决定回报方式
        /// </summary>
        /// <param name="stoppedLocally">是否为本实例的停止信号</param>
        public void Cancel(bool stoppedLocally)
        {
            try
            {
                if (_cancellation.IsCancellationRequested)
                    return;
                StoppedLocally = stoppedLocally;
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 节点已结束并释放
            }
        }

        /// <summary>
        /// 释放取消令牌源
        /// </summary>
        public void Dispose() => _cancellation.Dispose();
    }

    /// <summary>
    /// 账号准备或核验失败，回报为 AccountFailed，主实例据此把运行转为待处理
    /// </summary>
    private sealed class RemoteAccountException(string message, Exception inner) : Exception(message, inner);
}
