using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Pulonia.Remote;

/// <summary>
/// 子实例侧的远程节点执行：用本进程的执行器和游戏环境运行主实例派发的节点，并把结束结果回报主实例。
/// 不读写 Pulonia 状态文件；与主实例断开、本地停止时取消正在执行的节点
/// </summary>
public sealed class PuloniaRemoteNodeHost
{
    /// <summary>
    /// 延迟取得的通信通道
    /// </summary>
    private readonly Func<IPuloniaRemoteTransport> _transportFactory;

    /// <summary>
    /// 按任务类型查找的本进程执行器
    /// </summary>
    private readonly IReadOnlyDictionary<string, IPuloniaTaskExecutor> _executors;

    /// <summary>
    /// 本进程游戏中的节点执行与账号准备，与主实例本地执行共用同一实现（取租约 → 核验 UID → 建上下文 → 会话作用域 → 执行器）
    /// </summary>
    private readonly PuloniaLocalNodeRunner _localNodes;

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
        _localNodes = new PuloniaLocalNodeRunner(gameTaskCoordinator, accountSwitcher);
        _taskStopService = taskStopService;
        _logger = logger;
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
        // 执行标识是主实例每次新建的 GUID，不会重复，不再做重复检查
        _running[start.ExecutionId] = node;

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
        catch (PuloniaGenshinAccountException exception)
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
    /// 用本进程执行器执行快照节点；与主实例本地执行共用同一实现，游戏节点先取得输入并核验账号
    /// </summary>
    private async Task<PuloniaRemoteNodeFinished> ExecuteTaskAsync(PuloniaRemoteNodeStart start, CancellationToken ct)
    {
        var task = PuloniaTaskJson.Read<PuloniaTaskPreparedTask>(start.TaskJson!);
        var snapshot = PuloniaTaskJson.ReadSnapshot(start.SnapshotJson!);
        if (!_executors.TryGetValue(task.TaskType, out var executor))
            throw new InvalidOperationException($"执行实例没有注册 {task.TaskType} 执行器。");

        var outcome = await _localNodes.ExecuteAsync(executor, task, start.RunId, snapshot, start.Attempt,
            start.RequiresGameSession, start.ExpectedUid, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return PuloniaRemoteNodeFinished.FromOutcome(start.ExecutionId, outcome);
    }

    /// <summary>
    /// 在本进程的游戏中准备账号并返回完整核验 UID；续跑时只核验历史 UID
    /// </summary>
    private async Task<PuloniaRemoteNodeFinished> PrepareAccountAsync(PuloniaRemoteNodeStart start, CancellationToken ct)
    {
        // 账号资料来自主实例运行快照，主实例与子实例版本一致，按当前格式严格读取。
        var account = PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(start.AccountJson!);
        var uid = await _localNodes.PrepareAccountAsync(account, start.ExpectedUid, ct).ConfigureAwait(false);
        return new PuloniaRemoteNodeFinished
        {
            ExecutionId = start.ExecutionId,
            ResultKind = PuloniaRemoteNodeResultKind.Completed,
            Message = "账号已核验，实际 UID：" + uid,
            VerifiedUid = uid
        };
    }

    /// <summary>
    /// 回报节点结束；只发送一次，失败时记录日志。连接已断开时主实例已按断开处理该节点；
    /// 连接仍在却请求超时（罕见）时不再重试，主实例的节点会一直等到节点时限、用户停止或断开，再按超时或失败收尾
    /// </summary>
    private async Task SendFinishedAsync(PuloniaRemoteNodeFinished finished)
    {
        try
        {
            await Transport.FinishRemoteNodeAsync(finished, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException)
        {
            _logger.LogWarning(exception, "向主实例回报 Pulonia 节点结束失败：{ExecutionId}", finished.ExecutionId);
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

    // 账号准备或核验失败统一使用 PuloniaGenshinAccountException（与主实例本地执行共用），回报为 AccountFailed，
    // 主实例据此把运行转为待处理。
}
