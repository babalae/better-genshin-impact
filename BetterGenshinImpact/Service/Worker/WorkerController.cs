using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Instance.MessageHandlers;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Worker;

/// <summary>
/// GUI 侧的最小 Worker Controller：连接另一个 Windows 用户的 headless Worker，
/// 校验其身份，并转发 worker.status / task.* 请求。不负责 UI。
/// </summary>
public sealed class WorkerController : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<WorkerController> _logger;
    private readonly CancellationTokenSource _lifetimeCancellationTokenSource = new();
    private readonly SemaphoreSlim _connectSemaphore = new(1, 1);
    private readonly object _connectionLock = new();
    private readonly WorkerClientConnectionOwner _connectionOwner;

    private InstanceConnection? _connection;
    private string? _connectedWorkerSid;
    private WorkerStatusResponse? _lastStatus;
    private bool _disposed;

    public WorkerController(ILogger<WorkerController> logger)
    {
        _logger = logger;
        _connectionOwner = new WorkerClientConnectionOwner(this);
    }

    /// <summary>
    /// 已连接的 Worker 的 Windows 用户 SID；未连接时为 null
    /// </summary>
    public string? ConnectedWorkerSid
    {
        get
        {
            lock (_connectionLock)
            {
                return _connectedWorkerSid;
            }
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_connectionLock)
            {
                return _connection is not null;
            }
        }
    }

    /// <summary>
    /// 连接断开（含 Worker 退出）后触发
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// 当前进程是否处于「遥控 Worker」模式（已连接 Worker）。
    /// 该模式下本机不启动截图器、也不在本机执行任务：所有操作都下发给 Worker。
    /// </summary>
    public static bool IsRemoteControlled => App.GetService<WorkerController>()?.IsConnected == true;

    /// <summary>
    /// 收到 Worker 回传的用户提示（Worker 侧没有窗口可显示 Toast）。
    /// 可能在管道接收线程触发，订阅方需要自行切到 UI 线程
    /// </summary>
    public event EventHandler<WorkerNotice>? NoticeReceived;

    /// <summary>
    /// 收到 Worker 回传的日志批次（显示位置为「本地独立窗口」时才有）。
    /// 可能在管道接收线程触发，订阅方需要自行切到 UI 线程
    /// </summary>
    public event EventHandler<WorkerLogBatch>? LogReceived;

    /// <summary>
    /// 设置 Worker 日志的显示位置（<c>worker.logMode</c>）
    /// </summary>
    public Task<WorkerLogModeResponse> SetLogDisplayModeAsync(
        WorkerLogDisplayMode mode,
        int notificationIntervalSeconds,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            InstanceOperations.WorkerLogMode,
            new WorkerLogModeRequest
            {
                Mode = mode,
                NotificationIntervalSeconds = notificationIntervalSeconds
            },
            response => response?.ToObject<WorkerLogModeResponse>(InstanceIpcProtocol.Serializer)
                        ?? throw new WorkerUnavailableException("request_failed", "Worker 未返回日志设置。"),
            cancellationToken);
    }

    /// <summary>
    /// 最近一次已知的 Worker 状态；未连接时为 null。
    /// 连接成功、worker.status、capture.* 以及各命令之后都会刷新。
    /// </summary>
    public WorkerStatusResponse? LastStatus
    {
        get
        {
            lock (_connectionLock)
            {
                return _lastStatus;
            }
        }
    }

    /// <summary>
    /// Worker 状态变化。可能在任意线程触发，订阅方需要自行切到 UI 线程
    /// </summary>
    public event EventHandler<WorkerStatusResponse>? StatusChanged;

    /// <summary>
    /// 连接指定用户 SID 的 Worker，完成身份校验与 connection.open，并返回一次 worker.status。
    /// Worker 未运行时抛出 <see cref="WorkerUnavailableException"/>（ErrorCode = worker_offline）。
    /// </summary>
    public async Task<WorkerStatusResponse> ConnectAsync(
        string workerSid,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalizedSid = NormalizeSid(workerSid);

        await _connectSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        InstanceConnection? connection = null;
        NamedPipeClientStream? client = null;
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);

            var pipeName = InstancePipeNames.WorkerForUserSid(normalizedSid);
            client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough);

            using (var timeoutSource =
                   CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(ConnectTimeout);
                try
                {
                    await client.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException
                                                  or SocketException
                                                  or UnauthorizedAccessException
                                                  or TimeoutException
                                                  or OperationCanceledException)
                {
                    throw new WorkerUnavailableException(
                        "worker_offline",
                        $"无法连接 Worker（管道 {pipeName}）：{exception.GetBaseException().Message}",
                        exception);
                }
            }

            VerifyServerIdentity(client, normalizedSid);

            connection = new InstanceConnection(client, _connectionOwner, _logger);
            // 所有权转移给 InstanceConnection，失败路径由它统一释放管道
            client = null;

            connection.Start(_lifetimeCancellationTokenSource.Token);

            var openResponse = await connection.SendRequestAsync(
                InstanceOperations.ConnectionOpen,
                new ConnectionOpenRequest
                {
                    RequestedType = BetterGiInstanceType.Primary
                },
                RequestTimeout,
                cancellationToken).ConfigureAwait(false);
            if (openResponse.Success != true)
            {
                throw new WorkerUnavailableException(
                    "connection_failed",
                    openResponse.ErrorMessage ?? openResponse.ErrorCode ?? "Worker 拒绝了连接登记。");
            }

            var status = await SendStatusCoreAsync(connection, cancellationToken).ConfigureAwait(false);

            lock (_connectionLock)
            {
                _connection = connection;
                _connectedWorkerSid = normalizedSid;
            }

            _logger.LogInformation(
                "已连接 Worker：SID {WorkerSid}，管道 {PipeName}，状态 {State}",
                normalizedSid,
                pipeName,
                status.State);

            connection = null; // 所有权已转移给字段
            SetLastStatus(status);
            return status;
        }
        finally
        {
            client?.Dispose();
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }

            _connectSemaphore.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _connectSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _connectSemaphore.Release();
        }
    }

    public async Task<WorkerStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await SendAsync(
            InstanceOperations.WorkerStatus,
            null,
            ToStatusResponse,
            cancellationToken).ConfigureAwait(false);
        SetLastStatus(status);
        return status;
    }

    /// <summary>
    /// 启动 Worker 自己的截图器（运行环境 + 遮罩叠加层），返回最新状态
    /// </summary>
    public async Task<WorkerStatusResponse> StartCaptureAsync(CancellationToken cancellationToken = default)
    {
        var status = await SendAsync(
            InstanceOperations.CaptureStart,
            null,
            ToStatusResponse,
            cancellationToken).ConfigureAwait(false);
        SetLastStatus(status);
        return status;
    }

    /// <summary>
    /// 停止 Worker 自己的截图器，返回最新状态
    /// </summary>
    public async Task<WorkerStatusResponse> StopCaptureAsync(CancellationToken cancellationToken = default)
    {
        var status = await SendAsync(
            InstanceOperations.CaptureStop,
            null,
            ToStatusResponse,
            cancellationToken).ConfigureAwait(false);
        SetLastStatus(status);
        return status;
    }

    public Task<WorkerTaskStartResponse> StartTaskAsync(
        string? type,
        string? name,
        CancellationToken cancellationToken = default)
    {
        return StartTaskAsync(
            new WorkerTaskStartRequest { Type = type, Name = name },
            cancellationToken);
    }

    /// <summary>
    /// 按完整请求下发任务（连续执行、任务进度、一条龙、独立任务、脚本文件等）
    /// </summary>
    public Task<WorkerTaskStartResponse> StartTaskAsync(
        WorkerTaskStartRequest request,
        CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            InstanceOperations.TaskStart,
            request,
            response => response?.ToObject<WorkerTaskStartResponse>(InstanceIpcProtocol.Serializer)
                        ?? throw new WorkerUnavailableException("request_failed", "Worker 未返回任务信息。"),
            cancellationToken);
    }

    public Task<WorkerCommandResponse> StopTaskAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            InstanceOperations.TaskStop,
            null,
            ToCommandResponse,
            cancellationToken);
    }

    public Task<WorkerCommandResponse> PauseTaskAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            InstanceOperations.TaskPause,
            null,
            ToCommandResponse,
            cancellationToken);
    }

    public Task<WorkerCommandResponse> ResumeTaskAsync(CancellationToken cancellationToken = default)
    {
        return SendCommandAsync(
            InstanceOperations.TaskResume,
            null,
            ToCommandResponse,
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
        _lifetimeCancellationTokenSource.Cancel();
        _lifetimeCancellationTokenSource.Dispose();
        _connectSemaphore.Dispose();
    }

    private static WorkerCommandResponse ToCommandResponse(Newtonsoft.Json.Linq.JToken? data)
    {
        return data?.ToObject<WorkerCommandResponse>(InstanceIpcProtocol.Serializer)
               ?? new WorkerCommandResponse { State = WorkerState.Offline };
    }

    private static WorkerStatusResponse ToStatusResponse(Newtonsoft.Json.Linq.JToken? data)
    {
        return data?.ToObject<WorkerStatusResponse>(InstanceIpcProtocol.Serializer)
               ?? throw new WorkerUnavailableException("request_failed", "Worker 返回的状态为空。");
    }

    /// <summary>
    /// 发送命令并顺带刷新一次完整状态：命令响应只带 WorkerState，界面（主按钮等）需要完整状态
    /// </summary>
    private async Task<T> SendCommandAsync<T>(
        string operation,
        object? data,
        Func<Newtonsoft.Json.Linq.JToken?, T> selector,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync(operation, data, selector, cancellationToken).ConfigureAwait(false);
        await RefreshStatusQuietlyAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }

    private async Task RefreshStatusQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "刷新 Worker 状态失败");
        }
    }

    private void SetLastStatus(WorkerStatusResponse? status)
    {
        lock (_connectionLock)
        {
            _lastStatus = status;
        }

        if (status is not null)
        {
            StatusChanged?.Invoke(this, status);
        }
    }

    private async Task<T> SendAsync<T>(
        string operation,
        object? data,
        Func<Newtonsoft.Json.Linq.JToken?, T> selector,
        CancellationToken cancellationToken)
    {
        var connection = GetRequiredConnection();
        var response = await connection.SendRequestAsync(
            operation,
            data,
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        if (response.Success != true)
        {
            throw new WorkerUnavailableException(
                "request_failed",
                response.ErrorMessage ?? response.ErrorCode ?? $"Worker 请求 {operation} 失败。");
        }

        return selector(response.Data);
    }

    private async Task<WorkerStatusResponse> SendStatusCoreAsync(
        InstanceConnection connection,
        CancellationToken cancellationToken)
    {
        var response = await connection.SendRequestAsync(
            InstanceOperations.WorkerStatus,
            null,
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        if (response.Success != true)
        {
            throw new WorkerUnavailableException(
                "request_failed",
                response.ErrorMessage ?? response.ErrorCode ?? "worker.status 失败。");
        }

        return response.Data?.ToObject<WorkerStatusResponse>(InstanceIpcProtocol.Serializer)
               ?? throw new WorkerUnavailableException("request_failed", "Worker 返回的状态为空。");
    }

    private InstanceConnection GetRequiredConnection()
    {
        lock (_connectionLock)
        {
            return _connection
                   ?? throw new WorkerUnavailableException("worker_offline", "尚未连接 Worker。");
        }
    }

    private async Task DisconnectCoreAsync()
    {
        InstanceConnection? connection;
        lock (_connectionLock)
        {
            connection = _connection;
            _connection = null;
            _connectedWorkerSid = null;
        }

        SetLastStatus(null);

        if (connection is not null)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnNoticeReceived(WorkerNotice notice)
    {
        // 可能来自管道接收线程，等级仅用于日志；订阅方负责切 UI 线程
        _logger.LogInformation("Worker 提示（{Level}）：{Message}", notice.Level, notice.Message);
        try
        {
            NoticeReceived?.Invoke(this, notice);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "处理 Worker 提示时出现异常，已忽略。");
        }
    }

    private void OnLogReceived(WorkerLogBatch batch)
    {
        if (batch.Lines.Length == 0)
        {
            return;
        }

        try
        {
            LogReceived?.Invoke(this, batch);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "处理 Worker 日志时出现异常，已忽略。");
        }
    }

    private void OnConnectionClosed(InstanceConnection connection)
    {
        var wasCurrent = false;
        lock (_connectionLock)
        {
            if (ReferenceEquals(_connection, connection))
            {
                _connection = null;
                _connectedWorkerSid = null;
                wasCurrent = true;
            }
        }

        if (wasCurrent)
        {
            SetLastStatus(null);
            _logger.LogInformation("Worker 连接已断开");
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 用内核信息核对管道服务端确实是期望用户 SID 的进程，不能只依赖 Worker 自报。
    /// </summary>
    private void VerifyServerIdentity(NamedPipeClientStream client, string expectedSid)
    {
        if (!InstancePipePeerInfo.TryGetServerProcessAndSession(
                client.SafePipeHandle,
                out var serverProcessId,
                out _))
        {
            _logger.LogWarning("无法取得 Worker 管道服务端进程信息，跳过对端校验");
            return;
        }

        if (!InstancePipePeerInfo.TryGetProcessUserSid(serverProcessId, out var serverSid))
        {
            _logger.LogWarning("无法取得 Worker 进程 {ProcessId} 的用户 SID，跳过对端校验", serverProcessId);
            return;
        }

        if (!string.Equals(serverSid, expectedSid, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkerUnavailableException(
                "connection_failed",
                $"Worker 管道对端进程 {serverProcessId} 属于用户 {serverSid}，与期望的 {expectedSid} 不一致。");
        }
    }

    private static string NormalizeSid(string workerSid)
    {
        if (string.IsNullOrWhiteSpace(workerSid))
        {
            throw new WorkerUnavailableException("connection_failed", "Worker SID 不能为空。");
        }

        try
        {
            return new SecurityIdentifier(workerSid.Trim()).Value;
        }
        catch (Exception exception) when (exception is ArgumentException or SystemException)
        {
            throw new WorkerUnavailableException(
                "connection_failed",
                $"非法的 Worker SID：{workerSid}",
                exception);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Worker 连接的处理方。当前 Worker 不主动下发请求，进度以轮询 worker.status / task.status 获取。
    /// </summary>
    private sealed class WorkerClientConnectionOwner(WorkerController controller) : IInstanceConnectionOwner
    {
        public bool IsGameMouseModeEnabled => false;

        public Task<InstanceIpcEnvelope?> HandleRequestAsync(
            InstanceConnection connection,
            InstanceIpcEnvelope request,
            CancellationToken cancellationToken)
        {
            // Worker 单向回传的提示：不返回响应，直接交给 Controller 显示
            if (request.Operation == InstanceOperations.WorkerNotice)
            {
                var notice = request.Data?.ToObject<WorkerNotice>(InstanceIpcProtocol.Serializer);
                if (notice is not null)
                {
                    controller.OnNoticeReceived(notice);
                }
            }
            else if (request.Operation == InstanceOperations.WorkerLog)
            {
                var batch = request.Data?.ToObject<WorkerLogBatch>(InstanceIpcProtocol.Serializer);
                if (batch is not null)
                {
                    controller.OnLogReceived(batch);
                }
            }

            return Task.FromResult<InstanceIpcEnvelope?>(null);
        }

        public bool ReceiveRelativeMouseBatch(
            InstanceConnection connection,
            ulong firstSequence,
            System.Collections.Generic.IReadOnlyList<RelativeMouseSample> samples)
        {
            return false;
        }

        public void ReceiveRelativeMouseResult(
            InstanceConnection connection,
            RelativeMouseResult result)
        {
        }

        public void ConnectionClosed(InstanceConnection connection)
        {
            controller.OnConnectionClosed(connection);
        }
    }
}
