using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.Service.Instance.MessageHandlers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Worker;

/// <summary>
/// 无界面 Worker（--headless）的跨用户 IPC 服务端。
/// <para>
/// 复用 <see cref="InstanceConnection"/> 与帧协议，只新增 Worker 专属操作。
/// 不对外开放根管道，也不改变现有 Primary / ChildSession / WebView 的行为。
/// </para>
/// </summary>
internal sealed class WorkerIpcService : IHostedService, IAsyncDisposable, IInstanceConnectionOwner
{
    private readonly InstanceContext _context;
    private readonly WorkerTaskExecutor _executor;
    private readonly WorkerNoticeHub _noticeHub;
    private readonly ILogger<WorkerIpcService> _logger;
    private readonly CancellationTokenSource _lifetimeCancellationTokenSource = new();
    private readonly ConcurrentDictionary<InstanceConnection, byte> _connections = new();
    private readonly ConcurrentDictionary<InstanceConnection, byte> _authorized = new();

    private Task? _acceptLoopTask;
    private int _stopStarted;

    public WorkerIpcService(
        InstanceBootstrap bootstrap,
        WorkerTaskExecutor executor,
        WorkerNoticeHub noticeHub,
        ILogger<WorkerIpcService> logger)
    {
        _context = bootstrap.Context;
        _executor = executor;
        _noticeHub = noticeHub;
        _logger = logger;
    }

    public bool IsGameMouseModeEnabled => false;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_context.IsHeadless)
        {
            return Task.CompletedTask;
        }

        _acceptLoopTask = AcceptLoopAsync(_lifetimeCancellationTokenSource.Token);
        _logger.LogInformation(
            "Worker IPC 已启动：管道 {PipeName}，Worker SID {WorkerSid}，Controller SID {ControllerSid}",
            _context.WorkerPipeName,
            _context.WindowsUserSid,
            _context.ControllerUserSid ?? "(未指定)");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopStarted, 1) != 0)
        {
            return;
        }

        _lifetimeCancellationTokenSource.Cancel();
        foreach (var connection in _connections.Keys)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                                              or OperationCanceledException
                                              or ObjectDisposedException)
            {
                // 停止期间的正常清理
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifetimeCancellationTokenSource.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        if (!TryCreateFirstServer(out var server))
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                server ??= CreateServer();
                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var connection = new InstanceConnection(server, this, _logger);
                server = null;
                _connections.TryAdd(connection, 0);
                connection.Start(cancellationToken);
                _logger.LogDebug(
                    "Worker 收到新连接：客户端进程 {ProcessId}，Session {SessionId}",
                    connection.ClientProcessId,
                    connection.ClientSessionId);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(exception, "Worker 命名管道监听异常终止");
            }
        }
        finally
        {
            server?.Dispose();
        }
    }

    /// <summary>
    /// 首个实例使用 FirstPipeInstance：同一 Windows 用户同时只允许一个 Worker 占用该管道。
    /// </summary>
    private bool TryCreateFirstServer(out NamedPipeServerStream? server)
    {
        try
        {
            server = CreateServer(firstPipeInstance: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            server = null;
            _logger.LogError(
                exception,
                "Worker 管道 {PipeName} 已被占用，可能同一用户已存在正在运行的 Worker，本次 Worker 将退出。",
                _context.WorkerPipeName);
            ShutdownApplication();
            return false;
        }
    }

    private NamedPipeServerStream CreateServer(bool firstPipeInstance = false)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var ownerSid = identity.User
                       ?? throw new InvalidOperationException("无法取得当前 Windows 用户 SID。");

        var allowedSids = new List<SecurityIdentifier>
        {
            ownerSid,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)
        };
        if (_context.ControllerUserSid is { } controllerSid)
        {
            allowedSids.Add(new SecurityIdentifier(controllerSid));
        }

        return InstancePipeFactory.CreateServer(
            _context.WorkerPipeName,
            firstPipeInstance,
            ownerSid,
            allowedSids);
    }

    public async Task<InstanceIpcEnvelope?> HandleRequestAsync(
        InstanceConnection connection,
        InstanceIpcEnvelope request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await HandleRequestCoreAsync(connection, request).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // 任何请求异常都只返回失败响应，绝不让 Worker 进程崩溃
            _logger.LogWarning(exception, "处理 Worker 请求失败：{Operation}", request.Operation);
            return InstanceIpcEnvelope.Failure(
                request,
                "invalid_request",
                exception.GetBaseException().Message);
        }
    }

    private Task<InstanceIpcEnvelope?> HandleRequestCoreAsync(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        switch (request.Operation)
        {
            case InstanceOperations.Ping:
                return Task.FromResult<InstanceIpcEnvelope?>(
                    InstanceIpcEnvelope.Response(request, new WorkerCommandResponse
                    {
                        State = _executor.Snapshot().State
                    }));

            case InstanceOperations.ConnectionOpen:
                return Task.FromResult<InstanceIpcEnvelope?>(HandleConnectionOpen(connection, request));

            case InstanceOperations.WorkerStatus:
            case InstanceOperations.TaskStatus:
                RequireAuthorized(connection);
                return Task.FromResult<InstanceIpcEnvelope?>(
                    InstanceIpcEnvelope.Response(request, _executor.Snapshot()));

            case InstanceOperations.TaskStart:
                RequireAuthorized(connection);
                return Task.FromResult<InstanceIpcEnvelope?>(HandleTaskStart(request));

            case InstanceOperations.TaskStop:
                RequireAuthorized(connection);
                return Task.FromResult<InstanceIpcEnvelope?>(HandleTaskStop(request));

            case InstanceOperations.TaskPause:
                RequireAuthorized(connection);
                return Task.FromResult<InstanceIpcEnvelope?>(HandleTaskPause(request));

            case InstanceOperations.TaskResume:
                RequireAuthorized(connection);
                return Task.FromResult<InstanceIpcEnvelope?>(HandleTaskResume(request));

            case InstanceOperations.CaptureStart:
                RequireAuthorized(connection);
                return HandleCaptureStartAsync(request);

            case InstanceOperations.CaptureStop:
                RequireAuthorized(connection);
                return HandleCaptureStopAsync(request);

            case InstanceOperations.WorkerLogMode:
                RequireAuthorized(connection);
                return Task.FromResult<InstanceIpcEnvelope?>(HandleLogMode(request));

            default:
                return Task.FromResult<InstanceIpcEnvelope?>(
                    InstanceIpcEnvelope.Failure(
                        request,
                        "unsupported_operation",
                        $"不支持的 Worker 操作：{request.Operation}"));
        }
    }

    private InstanceIpcEnvelope HandleConnectionOpen(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        if (connection.RemoteEndpoint is not null)
        {
            throw new InvalidOperationException("当前管道连接已经完成登记。");
        }

        // 只认内核给出的真实客户端身份，绝不使用客户端上报的 SID。
        // 此刻已收到客户端首个请求帧，模拟客户端有稳定结果。
        var clientSid = connection.TryResolveClientUserSid();
        if (string.IsNullOrEmpty(clientSid))
        {
            throw new InvalidOperationException("无法取得命名管道客户端的 Windows 用户 SID，拒绝连接。");
        }

        if (!IsAllowedController(clientSid))
        {
            throw new InvalidOperationException($"Windows 用户 {clientSid} 不是本 Worker 允许的 Controller。");
        }

        var open = request.Data?.ToObject<ConnectionOpenRequest>(InstanceIpcProtocol.Serializer)
                   ?? new ConnectionOpenRequest
                   {
                       RequestedType = BetterGiInstanceType.Primary
                   };

        connection.RemoteEndpoint = new InstanceEndpoint
        {
            InstanceType = BetterGiInstanceType.Primary,
            ProcessId = connection.ClientProcessId ?? 0,
            WindowsSessionId = connection.ClientSessionId ?? 0,
            StartedAt = DateTimeOffset.UtcNow
        };
        _authorized.TryAdd(connection, 0);
        // SID 校验通过：之后 Worker 侧产生的提示都会回传给这个 Controller
        _noticeHub.Register(connection);

        _logger.LogInformation(
            "Controller 已连接 Worker：用户 {Sid}，进程 {ProcessId}，Session {SessionId}，声明用途 {RequestedType}",
            clientSid,
            connection.ClientProcessId,
            connection.ClientSessionId,
            open.RequestedType);

        return InstanceIpcEnvelope.Response(
            request,
            new ConnectionOpenResponse
            {
                Disposition = ConnectionOpenDisposition.Accepted,
                AssignedType = BetterGiInstanceType.Primary,
                RootProcessId = _context.ProcessId,
                RootSessionId = _context.WindowsSessionId
            });
    }

    private InstanceIpcEnvelope HandleTaskStart(InstanceIpcEnvelope request)
    {
        var start = request.Data?.ToObject<WorkerTaskStartRequest>(InstanceIpcProtocol.Serializer)
                    ?? new WorkerTaskStartRequest();
        if (!_executor.TryStart(start, out var error))
        {
            return InstanceIpcEnvelope.Failure(request, "task_rejected", error ?? "任务启动被拒绝。");
        }

        var snapshot = _executor.Snapshot();
        return InstanceIpcEnvelope.Response(
            request,
            new WorkerTaskStartResponse
            {
                Task = snapshot.CurrentTask ?? string.Empty,
                State = snapshot.State
            });
    }

    private InstanceIpcEnvelope HandleTaskStop(InstanceIpcEnvelope request)
    {
        if (!_executor.TryStop(out var error))
        {
            return InstanceIpcEnvelope.Failure(request, "task_rejected", error ?? "任务取消失败。");
        }

        return InstanceIpcEnvelope.Response(
            request,
            new WorkerCommandResponse { State = _executor.Snapshot().State });
    }

    private InstanceIpcEnvelope HandleTaskPause(InstanceIpcEnvelope request)
    {
        if (!_executor.TryPause(out var error))
        {
            return InstanceIpcEnvelope.Failure(request, "task_rejected", error ?? "任务暂停失败。");
        }

        return InstanceIpcEnvelope.Response(
            request,
            new WorkerCommandResponse { State = _executor.Snapshot().State });
    }

    private InstanceIpcEnvelope HandleTaskResume(InstanceIpcEnvelope request)
    {
        if (!_executor.TryResume(out var error))
        {
            return InstanceIpcEnvelope.Failure(request, "task_rejected", error ?? "任务继续失败。");
        }

        return InstanceIpcEnvelope.Response(
            request,
            new WorkerCommandResponse { State = _executor.Snapshot().State });
    }

    /// <summary>
    /// 启动 Worker 自己的截图器，成功与否都返回最新状态，便于 Controller 直接刷新
    /// </summary>
    private async Task<InstanceIpcEnvelope?> HandleCaptureStartAsync(InstanceIpcEnvelope request)
    {
        var (success, error) = await _executor.TryStartCaptureAsync().ConfigureAwait(false);
        if (!success)
        {
            return InstanceIpcEnvelope.Failure(request, "capture_failed", error ?? "启动 Worker 截图器失败。");
        }

        return InstanceIpcEnvelope.Response(request, _executor.Snapshot());
    }

    /// <summary>
    /// 停止 Worker 自己的截图器（运行中的任务随运行环境解绑一起取消）
    /// </summary>
    private async Task<InstanceIpcEnvelope?> HandleCaptureStopAsync(InstanceIpcEnvelope request)
    {
        var (success, error) = await _executor.TryStopCaptureAsync().ConfigureAwait(false);
        if (!success)
        {
            return InstanceIpcEnvelope.Failure(request, "capture_failed", error ?? "停止 Worker 截图器失败。");
        }

        return InstanceIpcEnvelope.Response(request, _executor.Snapshot());
    }

    private bool IsAllowedController(string clientSid)
    {
        return WorkerAuthorization.IsControllerAllowed(
            _context.WindowsUserSid,
            _context.ControllerUserSid,
            clientSid);
    }

    /// <summary>
    /// 应用 Controller 指定的日志显示位置
    /// </summary>
    private InstanceIpcEnvelope HandleLogMode(InstanceIpcEnvelope request)
    {
        var logModeRequest = request.Data?.ToObject<WorkerLogModeRequest>(InstanceIpcProtocol.Serializer);
        if (logModeRequest?.Mode is not { } mode)
        {
            return InstanceIpcEnvelope.Failure(
                request,
                "invalid_request",
                "worker.logMode 缺少 mode 字段。");
        }

        WorkerLogRouter.Configure(mode, logModeRequest.NotificationIntervalSeconds);
        // 切到「远程独立窗口」时立刻把空窗口弹出来，让 Worker 侧用户看到设置已生效
        App.GetService<WorkerLogWindowService>()?.HandleModeChanged(WorkerLogRouter.Mode);

        _logger.LogInformation(
            "Worker 日志显示位置已更新：{Mode}，通知聚合间隔 {Interval} 秒",
            WorkerLogRouter.Mode,
            WorkerLogRouter.NotificationIntervalSeconds);

        return InstanceIpcEnvelope.Response(
            request,
            new WorkerLogModeResponse
            {
                Mode = WorkerLogRouter.Mode,
                NotificationIntervalSeconds = WorkerLogRouter.NotificationIntervalSeconds
            });
    }

    private void RequireAuthorized(InstanceConnection connection)
    {
        if (!_authorized.ContainsKey(connection))
        {
            throw new InvalidOperationException("当前连接尚未完成授权登记，请先发送 connection.open。");
        }
    }

    public bool ReceiveRelativeMouseBatch(
        InstanceConnection connection,
        ulong firstSequence,
        IReadOnlyList<RelativeMouseSample> samples)
    {
        // Worker 不转发相对鼠标
        return false;
    }

    public void ReceiveRelativeMouseResult(
        InstanceConnection connection,
        RelativeMouseResult result)
    {
    }

    public void ConnectionClosed(InstanceConnection connection)
    {
        _authorized.TryRemove(connection, out _);
        _connections.TryRemove(connection, out _);
        _noticeHub.Unregister(connection);
        _logger.LogInformation(
            "Worker 连接已断开：客户端进程 {ProcessId}，SID {Sid}",
            connection.ClientProcessId,
            connection.ClientUserSid ?? "(未知)");
        _ = connection.DisposeAsync().AsTask();
    }

    private static void ShutdownApplication()
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        _ = application.Dispatcher.BeginInvoke(new Action(application.Shutdown));
    }
}
