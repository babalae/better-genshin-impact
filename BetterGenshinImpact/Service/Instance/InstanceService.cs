using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Service.Instance.MessageHandlers;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Instance;

public sealed class InstanceService : IHostedService, IAsyncDisposable, IPuloniaRemoteTransport
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly InstanceBootstrap _bootstrap;
    private readonly ILogger<InstanceService> _logger;
    private readonly InstanceMessageState _messageState = new();
    private readonly InstanceRequestHandler _requestHandler;
    private readonly RelativeMouseMessageHandler _relativeMouseMessageHandler;
    /// <summary>
    /// 当前进程独立任务的运行状态与启动停止入口。
    /// </summary>
    private readonly SoloTaskExecutionService _soloTaskExecutionService;
    /// <summary>
    /// 根实例缓存的在线子实例独立任务状态，键为子实例进程标识。
    /// </summary>
    private readonly ConcurrentDictionary<int, SoloTaskStatusSnapshot> _remoteSoloTaskStatuses = new();
    private readonly CancellationTokenSource _lifetimeCancellationTokenSource = new();
    private readonly ConcurrentQueue<string[]> _pendingActivations = new();
    private readonly object _activationLock = new();
    private readonly object _rootConnectionLock = new();

    private Task? _acceptLoopTask;
    private Task? _rootConnectionLoopTask;
    private InstanceConnection? _rootConnection;
    private bool _applicationReady;
    private int _stopStarted;

    public InstanceService(
        InstanceBootstrap bootstrap,
        IServiceProvider serviceProvider,
        RawInputMonitor rawInputMonitor,
        IConfigService configService,
        SoloTaskExecutionService soloTaskExecutionService,
        ILogger<InstanceService> logger)
    {
        _bootstrap = bootstrap;
        _logger = logger;
        _soloTaskExecutionService = soloTaskExecutionService;
        _soloTaskExecutionService.StatusChanged += OnLocalSoloTaskStatusChanged;
        _relativeMouseMessageHandler = new RelativeMouseMessageHandler(
            Context,
            serviceProvider,
            rawInputMonitor,
            IsRootConnection,
            logger);
        if (Context.InstanceType == BetterGiInstanceType.Primary)
        {
            _relativeMouseMessageHandler.SetGameMouseModeEnabled(
                configService.Get().ChildSessionConfig.GameMouseModeEnabled);
        }
        // Pulonia 远程节点的宿主与派发代理依赖执行器和 Pulonia 服务，收到请求时才从容器取得，避免构造期循环依赖
        var puloniaRemoteMessageHandler = new PuloniaRemoteMessageHandler(
            Context,
            serviceProvider.GetRequiredService<PuloniaRemoteNodeHost>,
            serviceProvider.GetRequiredService<PuloniaRemoteExecutionBroker>);
        _requestHandler = new InstanceRequestHandler(
            Context,
            _messageState,
            _relativeMouseMessageHandler,
            EnqueueActivation,
            DispatchWebViewMessage,
            _soloTaskExecutionService,
            ReceiveSoloTaskStatus,
            NotifyConnectedInstancesChanged,
            puloniaRemoteMessageHandler,
            logger);
    }

    /// <inheritdoc />
    public event Action<int>? RemoteInstanceDisconnected;

    /// <inheritdoc />
    public event Action? RootConnectionLost;

    /// <summary>
    /// 主实例：按执行目标查找当前在线的子实例。桌面分身按会话匹配，云原神按实例名匹配（不区分大小写）
    /// </summary>
    /// <param name="target">执行目标</param>
    /// <returns>在线子实例；不在线时为 null</returns>
    public InstanceEndpoint? FindPuloniaTargetEndpoint(PuloniaTaskExecutionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return GetConnectedChildInstances().FirstOrDefault(endpoint => target.Kind switch
        {
            PuloniaTaskExecutionTargetKind.ChildSession => endpoint.InstanceType == BetterGiInstanceType.ChildSession
                                                           && endpoint.WindowsSessionId == target.WindowsSessionId,
            PuloniaTaskExecutionTargetKind.WebView => endpoint.InstanceType == BetterGiInstanceType.WebView
                                                      && string.Equals(endpoint.InstanceName, target.InstanceName,
                                                          StringComparison.OrdinalIgnoreCase),
            _ => false
        });
    }

    /// <inheritdoc />
    int? IPuloniaRemoteTransport.ResolveTargetProcessId(PuloniaTaskExecutionTarget target)
        => FindPuloniaTargetEndpoint(target)?.ProcessId;

    /// <inheritdoc />
    Task IPuloniaRemoteTransport.StartRemoteNodeAsync(int processId, PuloniaRemoteNodeStart start, CancellationToken ct)
        => SendPuloniaRequestToChildAsync(processId, InstanceOperations.PuloniaNodeStart, start, ct);

    /// <inheritdoc />
    Task IPuloniaRemoteTransport.CancelRemoteNodeAsync(int processId, Guid executionId, CancellationToken ct)
        => SendPuloniaRequestToChildAsync(processId, InstanceOperations.PuloniaNodeCancel,
            new PuloniaRemoteNodeCancel { ExecutionId = executionId }, ct);

    /// <inheritdoc />
    async Task IPuloniaRemoteTransport.FinishRemoteNodeAsync(PuloniaRemoteNodeFinished finished, CancellationToken ct)
    {
        var response = await GetRequiredRootConnection().SendRequestAsync(
            InstanceOperations.PuloniaNodeFinished,
            finished,
            RequestTimeout,
            ct).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
    }

    /// <summary>
    /// 主实例向指定子实例发送 Pulonia 节点控制请求
    /// </summary>
    /// <exception cref="InvalidOperationException">当前不是主实例、目标不在线或目标拒绝</exception>
    private async Task SendPuloniaRequestToChildAsync(int processId, string operation, object data, CancellationToken ct)
    {
        if (!Context.IsRoot)
        {
            throw new InvalidOperationException("只有主实例可以向子实例派发 Pulonia 节点。");
        }

        var endpoint = GetConnectedChildInstances().FirstOrDefault(item => item.ProcessId == processId)
                       ?? throw new InvalidOperationException("执行实例当前不在线。");
        var target = GetRequiredChildConnection(endpoint);
        var response = await target.Connection.SendRequestAsync(operation, data, RequestTimeout, ct)
            .ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
    }

    /// <summary>
    /// 安全通知子实例断开，订阅方异常不影响连接清理
    /// </summary>
    private void RaiseRemoteInstanceDisconnected(int processId)
    {
        try
        {
            RemoteInstanceDisconnected?.Invoke(processId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "通知子实例断开失败");
        }
    }

    /// <summary>
    /// 安全通知与根实例的连接已断开
    /// </summary>
    private void RaiseRootConnectionLost()
    {
        try
        {
            RootConnectionLost?.Invoke();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "通知根实例连接断开失败");
        }
    }

    /// <summary>
    /// 主实例中的在线子实例集合发生变化时触发。
    /// 事件可能从命名管道后台线程触发，UI 订阅方需要自行切换到 UI 线程。
    /// </summary>
    public event EventHandler? ConnectedInstancesChanged;

    /// <summary>
    /// 本实例或任一在线子实例的独立任务状态发生变化时触发。
    /// </summary>
    public event EventHandler? SoloTaskStatusesChanged;

    public event EventHandler<WebViewMessageReceivedEventArgs>? WebViewMessageReceived;

    public InstanceContext Context => _bootstrap.Context;

    public bool IsGameMouseModeEnabled =>
        _relativeMouseMessageHandler.IsGameMouseModeEnabled;

    public void SetGameMouseModeEnabled(bool enabled)
    {
        _relativeMouseMessageHandler.SetGameMouseModeEnabled(enabled);
    }

    /// <summary>
    /// 返回当前界面允许管理的实例列表。只有主实例会列出在线子实例。
    /// </summary>
    public InstanceEndpoint[] GetSoloTaskTargets()
    {
        if (!Context.IsRoot)
        {
            return [Context.ToEndpoint()];
        }

        return [Context.ToEndpoint(), .. GetConnectedChildInstances()];
    }

    /// <summary>
    /// 返回指定实例最近一次同步到本进程的独立任务状态。
    /// </summary>
    public SoloTaskStatusSnapshot GetSoloTaskStatus(InstanceEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (IsCurrentInstance(endpoint))
        {
            return _soloTaskExecutionService.GetStatus();
        }

        return _remoteSoloTaskStatuses.TryGetValue(endpoint.ProcessId, out var status)
            ? status
            : SoloTaskStatusSnapshot.Idle();
    }

    /// <summary>
    /// 主动查询指定实例的真实独立任务状态；用于请求超时后校正本地缓存。
    /// </summary>
    public async Task<SoloTaskStatusSnapshot> RefreshSoloTaskStatusAsync(
        InstanceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (IsCurrentInstance(endpoint))
        {
            return _soloTaskExecutionService.GetStatus();
        }

        if (!Context.IsRoot)
        {
            throw new InvalidOperationException("只有主实例可以查询其他实例中的独立任务。");
        }

        var target = GetRequiredChildConnection(endpoint);
        var response = await target.Connection.SendRequestAsync(
            InstanceOperations.TaskStatusGet,
            null,
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
        var status = response.Data?.ToObject<SoloTaskStatusSnapshot>(InstanceIpcProtocol.Serializer)
                     ?? throw new InvalidDataException("目标实例返回的独立任务状态为空。");
        UpdateRemoteSoloTaskStatus(endpoint, status);
        return status;
    }

    /// <summary>
    /// 在指定实例启动独立任务；远程实例只接受主实例转发。
    /// </summary>
    public async Task<SoloTaskOperationResponse> StartSoloTaskAsync(
        InstanceEndpoint endpoint,
        SoloTaskStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(request);
        if (IsCurrentInstance(endpoint))
        {
            return await _soloTaskExecutionService.StartAsync(request).ConfigureAwait(false);
        }

        if (!Context.IsRoot)
        {
            throw new InvalidOperationException("只有主实例可以控制其他实例中的独立任务。");
        }

        var target = GetRequiredChildConnection(endpoint);
        var response = await target.Connection.SendRequestAsync(
            InstanceOperations.TaskStart,
            request,
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
        var operation = response.Data?.ToObject<SoloTaskOperationResponse>(InstanceIpcProtocol.Serializer)
                        ?? throw new InvalidDataException("目标实例返回的独立任务启动结果为空。");
        UpdateRemoteSoloTaskStatus(endpoint, operation.Status);
        return operation;
    }

    /// <summary>
    /// 停止指定实例中运行标识匹配的独立任务。
    /// </summary>
    public async Task<SoloTaskOperationResponse> StopSoloTaskAsync(
        InstanceEndpoint endpoint,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var request = new SoloTaskStopRequest { RunId = runId };
        if (IsCurrentInstance(endpoint))
        {
            return await _soloTaskExecutionService.StopAsync(request).ConfigureAwait(false);
        }

        if (!Context.IsRoot)
        {
            throw new InvalidOperationException("只有主实例可以控制其他实例中的独立任务。");
        }

        var target = GetRequiredChildConnection(endpoint);
        var response = await target.Connection.SendRequestAsync(
            InstanceOperations.TaskStop,
            request,
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
        var operation = response.Data?.ToObject<SoloTaskOperationResponse>(InstanceIpcProtocol.Serializer)
                        ?? throw new InvalidDataException("目标实例返回的独立任务停止结果为空。");
        UpdateRemoteSoloTaskStatus(endpoint, operation.Status);
        return operation;
    }

    /// <summary>
    /// 获取主实例当前已完成 IPC 登记的在线子实例快照。
    /// 桌面分身优先显示，云原神实例按名称排序。
    /// </summary>
    public InstanceEndpoint[] GetConnectedChildInstances()
    {
        if (Context.InstanceType != BetterGiInstanceType.Primary)
        {
            return [];
        }

        lock (_messageState.RegistrationLock)
        {
            return OrderConnectedChildInstances(
                _messageState.BetterGiConnectionsBySession.Values
                    .Concat(_messageState.WebViewConnectionsByProcessId.Values)
                    .Select(x => x.Endpoint));
        }
    }

    /// <summary>
    /// 检查主实例当前是否仍连接着 WebView2 网页版实例。
    /// </summary>
    public bool HasConnectedWebViewInstance()
    {
        return GetConnectedChildInstances()
            .Any(endpoint => endpoint.InstanceType == BetterGiInstanceType.WebView);
    }

    /// <summary>
    /// 请求主实例当前连接的指定子实例显示并激活自身主窗口。
    /// </summary>
    /// <param name="endpoint">需要激活的在线子实例。</param>
    /// <param name="cancellationToken">取消等待 IPC 响应的令牌。</param>
    public async Task ActivateChildInstanceAsync(
        InstanceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Context.InstanceType != BetterGiInstanceType.Primary)
        {
            throw new InvalidOperationException("只有主实例可以激活子实例。");
        }

        var target = GetRequiredChildConnection(endpoint);
        var response = await target.Connection.SendRequestAsync(
            InstanceOperations.ActivationDispatch,
            new ActivationDispatchRequest(),
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
    }

    /// <summary>
    /// 请求主实例当前连接的指定子实例正常退出。
    /// </summary>
    /// <param name="endpoint">需要关闭的在线子实例。</param>
    /// <param name="cancellationToken">取消等待 IPC 响应的令牌。</param>
    public async Task CloseChildInstanceAsync(
        InstanceEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (Context.InstanceType != BetterGiInstanceType.Primary)
        {
            throw new InvalidOperationException("只有主实例可以关闭子实例。");
        }

        var target = GetRequiredChildConnection(endpoint);
        var response = await target.Connection.SendRequestAsync(
            InstanceOperations.ApplicationShutdown,
            null,
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
    }

    /// <summary>
    /// 按标题栏展示规则为在线子实例生成稳定顺序。
    /// </summary>
    internal static InstanceEndpoint[] OrderConnectedChildInstances(
        IEnumerable<InstanceEndpoint> endpoints)
    {
        return endpoints
            .OrderBy(endpoint => endpoint.InstanceType == BetterGiInstanceType.ChildSession ? 0 : 1)
            .ThenBy(endpoint => endpoint.InstanceType == BetterGiInstanceType.ChildSession
                ? endpoint.WindowsSessionId.ToString("D10")
                : endpoint.InstanceName ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(endpoint => endpoint.ProcessId)
            .ToArray();
    }

    /// <summary>
    /// 根据端点标识重新解析当前有效连接，避免对已经被替换的旧连接执行操作。
    /// </summary>
    private RegisteredInstanceConnection GetRequiredChildConnection(InstanceEndpoint endpoint)
    {
        lock (_messageState.RegistrationLock)
        {
            return endpoint.InstanceType switch
            {
                BetterGiInstanceType.ChildSession
                    when _messageState.BetterGiConnectionsBySession.TryGetValue(
                        endpoint.WindowsSessionId,
                        out var child)
                         && child.Endpoint.ProcessId == endpoint.ProcessId => child,
                BetterGiInstanceType.WebView
                    when _messageState.WebViewConnectionsByProcessId.TryGetValue(
                        endpoint.ProcessId,
                        out var webView)
                         && webView.Endpoint.WindowsSessionId == endpoint.WindowsSessionId => webView,
                _ => throw new InvalidOperationException("目标子实例当前不在线。")
            };
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Context.InstanceType == BetterGiInstanceType.Primary)
        {
            var firstServer = _bootstrap.TakeFirstServer()
                              ?? throw new InvalidOperationException(
                                  "根实例未取得首个命名管道服务端。");
            _acceptLoopTask = AcceptLoopAsync(
                firstServer,
                _lifetimeCancellationTokenSource.Token);
        }
        else
        {
            _rootConnectionLoopTask = RootConnectionLoopAsync(
                _bootstrap.TakeFirstRootConnection(),
                _lifetimeCancellationTokenSource.Token);
        }

        _logger.LogInformation(
            "实例 IPC v2 已启动：{InstanceType}，进程 {ProcessId}，Session {SessionId}，根管道 {PipeName}",
            Context.InstanceType,
            Context.ProcessId,
            Context.WindowsSessionId,
            Context.RootPipeName);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopStarted, 1) != 0)
        {
            return;
        }

        _lifetimeCancellationTokenSource.Cancel();
        _relativeMouseMessageHandler.Stop();

        InstanceConnection? rootConnection;
        lock (_rootConnectionLock)
        {
            rootConnection = _rootConnection;
            _rootConnection = null;
        }
        if (rootConnection is not null)
        {
            await rootConnection.DisposeAsync().ConfigureAwait(false);
        }

        var connections = _messageState.BetterGiConnectionsBySession.Values
            .Concat(_messageState.WebViewConnectionsByProcessId.Values)
            .Select(x => x.Connection)
            .Distinct()
            .ToArray();
        foreach (var connection in connections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        await AwaitBackgroundTaskAsync(_acceptLoopTask).ConfigureAwait(false);
        await AwaitBackgroundTaskAsync(_rootConnectionLoopTask).ConfigureAwait(false);
    }

    public void MarkApplicationReady()
    {
        var pendingActivations = new List<string[]>();
        lock (_activationLock)
        {
            _applicationReady = true;
            while (_pendingActivations.TryDequeue(out var args))
            {
                pendingActivations.Add(args);
            }
        }

        foreach (var args in pendingActivations)
        {
            DispatchActivation(args);
        }
    }

    public async Task<InstanceEndpoint[]> GetVisibleWebViewsAsync(
        CancellationToken cancellationToken = default)
    {
        if (Context.InstanceType == BetterGiInstanceType.Primary)
        {
            return _messageState.WebViewConnectionsByProcessId.Values
                .Select(x => x.Endpoint)
                .OrderBy(x => x.InstanceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ProcessId)
                .ToArray();
        }
        if (Context.InstanceType == BetterGiInstanceType.WebView)
        {
            return [];
        }

        var rootConnection = GetRequiredRootConnection();
        var response = await rootConnection.SendRequestAsync(
            InstanceOperations.WebViewList,
            null,
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
        return response.Data?.ToObject<WebViewListResponse>(InstanceIpcProtocol.Serializer)
                   ?.Endpoints
               ?? [];
    }

    public async Task SendWebViewMessageAsync(
        int targetProcessId,
        string operation,
        JToken? data = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new ArgumentException("WebView 操作名称不能为空。", nameof(operation));
        }
        if (Context.InstanceType == BetterGiInstanceType.WebView)
        {
            throw new InvalidOperationException("WebView 不能向其他 WebView 发送消息。");
        }

        if (Context.InstanceType == BetterGiInstanceType.Primary)
        {
            if (!_messageState.WebViewConnectionsByProcessId.TryGetValue(
                    targetProcessId,
                    out var target))
            {
                throw new InvalidOperationException(
                    $"WebView 进程 {targetProcessId} 当前不在线。");
            }

            var targetResponse = await target.Connection.SendRequestAsync(
                InstanceOperations.WebViewMessage,
                new WebViewMessage
                {
                    SourceProcessId = Context.ProcessId,
                    Operation = operation,
                    Data = data
                },
                RequestTimeout,
                cancellationToken).ConfigureAwait(false);
            EnsureSuccessfulResponse(targetResponse);
            return;
        }

        var rootConnection = GetRequiredRootConnection();
        var response = await rootConnection.SendRequestAsync(
            InstanceOperations.WebViewSend,
            new WebViewSendRequest
            {
                TargetProcessId = targetProcessId,
                Operation = operation,
                Data = data
            },
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
    }

    internal async Task<InstanceIpcEnvelope?> HandleRequestAsync(
        InstanceConnection connection,
        InstanceIpcEnvelope request,
        CancellationToken cancellationToken)
    {
        return await _requestHandler.HandleAsync(
            connection,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    internal bool ReceiveRelativeMouseBatch(
        InstanceConnection connection,
        ulong firstSequence,
        IReadOnlyList<RelativeMouseSample> samples)
    {
        return _relativeMouseMessageHandler.HandleBatch(
            connection,
            firstSequence,
            samples);
    }

    internal void ReceiveRelativeMouseResult(
        InstanceConnection connection,
        RelativeMouseResult result)
    {
        _relativeMouseMessageHandler.HandleResult(connection, result);
    }

    private bool IsRootConnection(InstanceConnection connection)
    {
        lock (_rootConnectionLock)
        {
            return ReferenceEquals(_rootConnection, connection);
        }
    }

    internal void ConnectionClosed(InstanceConnection connection)
    {
        var connectedInstancesChanged = false;
        int? disconnectedChildSessionId = null;
        if (Context.InstanceType == BetterGiInstanceType.Primary
            && connection.RemoteEndpoint is { } endpoint)
        {
            lock (_messageState.RegistrationLock)
            {
                if (endpoint.InstanceType == BetterGiInstanceType.ChildSession
                    && _messageState.BetterGiConnectionsBySession.TryGetValue(
                        endpoint.WindowsSessionId,
                        out var child)
                    && ReferenceEquals(child.Connection, connection))
                {
                    _messageState.BetterGiConnectionsBySession.TryRemove(
                        endpoint.WindowsSessionId,
                        out _);
                    disconnectedChildSessionId = endpoint.WindowsSessionId;
                    connectedInstancesChanged = true;
                }
                else if (endpoint.InstanceType == BetterGiInstanceType.WebView
                         && _messageState.WebViewConnectionsByProcessId.TryGetValue(
                             endpoint.ProcessId,
                             out var webView)
                         && ReferenceEquals(webView.Connection, connection))
                {
                    _messageState.WebViewConnectionsByProcessId.TryRemove(
                        endpoint.ProcessId,
                        out _);
                    connectedInstancesChanged = true;
                }
            }

            if (disconnectedChildSessionId is not null)
            {
                _relativeMouseMessageHandler.RemoveTarget(disconnectedChildSessionId.Value);
                _logger.LogInformation(
                    "桌面分身 BetterGI 已断开：进程 {ProcessId}，Session {SessionId}",
                    endpoint.ProcessId,
                    endpoint.WindowsSessionId);
            }
            else if (connectedInstancesChanged)
            {
                _logger.LogInformation(
                    "WebView 已断开：进程 {ProcessId}，Session {SessionId}",
                    endpoint.ProcessId,
                    endpoint.WindowsSessionId);
            }

            if (connectedInstancesChanged)
            {
                _remoteSoloTaskStatuses.TryRemove(endpoint.ProcessId, out _);
                // 发往该子实例的 Pulonia 节点结果已无法送达，让等待中的节点立即失败
                RaiseRemoteInstanceDisconnected(endpoint.ProcessId);
                NotifyConnectedInstancesChanged();
                NotifySoloTaskStatusesChanged();
            }
        }

        lock (_rootConnectionLock)
        {
            if (ReferenceEquals(_rootConnection, connection))
            {
                _rootConnection = null;
            }
        }

        _ = connection.DisposeAsync().AsTask();
    }

    public async ValueTask DisposeAsync()
    {
        _soloTaskExecutionService.StatusChanged -= OnLocalSoloTaskStatusChanged;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifetimeCancellationTokenSource.Dispose();
        _bootstrap.Dispose();
    }

    private async Task AcceptLoopAsync(
        NamedPipeServerStream firstServer,
        CancellationToken cancellationToken)
    {
        NamedPipeServerStream? server = firstServer;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                server ??= InstancePipeFactory.CreateServer(
                    Context.RootPipeName,
                    firstPipeInstance: false);
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
                connection.Start(cancellationToken);
                _ = ObserveAcceptedConnectionAsync(connection);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(exception, "根实例命名管道监听异常终止");
            }
        }
        finally
        {
            server?.Dispose();
        }
    }

    private static async Task ObserveAcceptedConnectionAsync(InstanceConnection connection)
    {
        await connection.Completion.ConfigureAwait(false);
    }

    private async Task RootConnectionLoopAsync(
        InitialRootConnection? initialRootConnection,
        CancellationToken cancellationToken)
    {
        var pendingInitialConnection = initialRootConnection;
        var includeActivationArguments = pendingInitialConnection is null;
        var restartFromProcessId = CommandLineOptions.Instance.RestartFromProcessId;

        while (!cancellationToken.IsCancellationRequested)
        {
            InstanceConnection? connection = null;
            var registeredAsRoot = false;
            try
            {
                ConnectionOpenResponse openResponse;
                if (pendingInitialConnection is not null)
                {
                    openResponse = pendingInitialConnection.Response;
                    connection = new InstanceConnection(
                        pendingInitialConnection.Client,
                        this,
                        _logger);
                    pendingInitialConnection = null;
                }
                else
                {
                    var client = new NamedPipeClientStream(
                        ".",
                        Context.RootPipeName,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough);
                    await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    connection = new InstanceConnection(client, this, _logger);
                    connection.Start(cancellationToken);

                    var openResult = await connection.SendRequestAsync(
                        InstanceOperations.ConnectionOpen,
                        new ConnectionOpenRequest
                        {
                            RequestedType = Context.InstanceType,
                            InstanceName = Context.InstanceName,
                            RestartFromProcessId = restartFromProcessId,
                            Arguments = includeActivationArguments
                                ? Environment.GetCommandLineArgs()
                                : []
                        },
                        RequestTimeout,
                        cancellationToken).ConfigureAwait(false);
                    EnsureSuccessfulResponse(openResult);
                    openResponse =
                        openResult.Data?.ToObject<ConnectionOpenResponse>(
                            InstanceIpcProtocol.Serializer)
                        ?? throw new InvalidDataException("根实例连接响应缺少数据。");
                }

                if (openResponse.Disposition
                    == ConnectionOpenDisposition.ActivationForwarded)
                {
                    RequestApplicationShutdown();
                    return;
                }
                if (openResponse.AssignedType != Context.InstanceType)
                {
                    throw new InvalidOperationException(
                        $"根实例分配了不匹配的客户端类型：{openResponse.AssignedType}。");
                }

                Context.SetRootSessionId(openResponse.RootSessionId);
                connection.RemoteEndpoint = new InstanceEndpoint
                {
                    InstanceType = BetterGiInstanceType.Primary,
                    ProcessId = openResponse.RootProcessId,
                    WindowsSessionId = openResponse.RootSessionId,
                    StartedAt = DateTimeOffset.UtcNow
                };
                if (!connection.IsStarted)
                {
                    connection.Start(cancellationToken);
                }
                if (connection.Completion.IsCompleted)
                {
                    throw new IOException("根实例连接在完成登记前已经关闭。");
                }

                lock (_rootConnectionLock)
                {
                    _rootConnection = connection;
                }
                registeredAsRoot = true;

                includeActivationArguments = false;
                restartFromProcessId = null;
                _logger.LogInformation(
                    "已连接 BetterGI 根实例：进程 {ProcessId}，Session {SessionId}",
                    openResponse.RootProcessId,
                    openResponse.RootSessionId);

                if (Context.InstanceType == BetterGiInstanceType.ChildSession)
                {
                    var subscribeResponse = await connection.SendRequestAsync(
                        InstanceOperations.RelativeMouseSubscribe,
                        null,
                        RequestTimeout,
                        cancellationToken).ConfigureAwait(false);
                    EnsureSuccessfulResponse(subscribeResponse);
                }

                await connection.Completion.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or TimeoutException
                                              or OperationCanceledException
                                              or ObjectDisposedException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        exception,
                        "连接 BetterGI 根实例失败，稍后重试：{PipeName}",
                        Context.RootPipeName);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                              or InvalidDataException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(
                        exception,
                        "连接 BetterGI 根实例时发生不可恢复的协议错误，停止重连：{PipeName}",
                        Context.RootPipeName);
                    RequestApplicationShutdown();
                }
                return;
            }
            finally
            {
                lock (_rootConnectionLock)
                {
                    if (ReferenceEquals(_rootConnection, connection))
                    {
                        _rootConnection = null;
                    }
                }

                if (connection is not null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }

                // 主实例派发的 Pulonia 节点结果已无法回报，重连后也不会再有人等待它们
                if (registeredAsRoot)
                {
                    RaiseRootConnectionLost();
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private InstanceConnection GetRequiredRootConnection()
    {
        lock (_rootConnectionLock)
        {
            return _rootConnection
                   ?? throw new InvalidOperationException("当前尚未连接 BetterGI 根实例。");
        }
    }

    private void EnqueueActivation(string[] args)
    {
        lock (_activationLock)
        {
            if (!_applicationReady)
            {
                _pendingActivations.Enqueue(args);
                return;
            }
        }

        DispatchActivation(args);
    }

    private void DispatchActivation(string[] args)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            var commandLineOptions = CommandLineOptions.Parse(args);
            if (commandLineOptions.IsPuloniaAction)
            {
                // 定时唤起转发到主实例，不激活主窗口，不与用户正在操作的桌面争抢焦点。
                _ = App.GetService<Pulonia.Services.PuloniaTaskTriggerHost>()?.HandleActivationAsync(commandLineOptions);
                return;
            }
            var mainWindow = Application.Current.MainWindow;
            mainWindow?.Show();
            mainWindow?.Activate();
            if (mainWindow is not null)
            {
                SystemControl.RestoreWindow(new WindowInteropHelper(mainWindow).Handle);
            }

            App.GetService<HomePageViewModel>()?.HandleActivation(commandLineOptions);
        }));
    }

    private void DispatchWebViewMessage(WebViewMessage message)
    {
        WebViewMessageReceived?.Invoke(
            this,
            new WebViewMessageReceivedEventArgs(
                message.SourceProcessId,
                message.Operation,
                message.Data));
    }

    /// <summary>
    /// 将本进程状态变化发布给本地 UI；子实例同时尽力上报给根实例。
    /// </summary>
    private void OnLocalSoloTaskStatusChanged(object? sender, SoloTaskStatusChangedEventArgs e)
    {
        NotifySoloTaskStatusesChanged();
        if (Context.IsRoot)
        {
            return;
        }

        _ = ReportSoloTaskStatusToRootAsync(e.Status);
    }

    /// <summary>
    /// 子实例通过已经登记的根连接上报自身独立任务状态。
    /// </summary>
    private async Task ReportSoloTaskStatusToRootAsync(SoloTaskStatusSnapshot status)
    {
        try
        {
            var connection = GetRequiredRootConnection();
            var response = await connection.SendRequestAsync(
                InstanceOperations.TaskStatusChanged,
                status,
                RequestTimeout,
                _lifetimeCancellationTokenSource.Token).ConfigureAwait(false);
            EnsureSuccessfulResponse(response);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or IOException
                                          or TimeoutException
                                          or OperationCanceledException)
        {
            if (!_lifetimeCancellationTokenSource.IsCancellationRequested)
            {
                _logger.LogDebug(exception, "向根实例上报独立任务状态失败，重连后将重新同步");
            }
        }
    }

    /// <summary>
    /// 根实例接收子实例主动上报的任务状态并更新缓存。
    /// </summary>
    private void ReceiveSoloTaskStatus(
        InstanceConnection connection,
        SoloTaskStatusSnapshot status)
    {
        var endpoint = connection.RemoteEndpoint
                       ?? throw new InvalidOperationException("任务状态上报连接尚未完成登记。");
        UpdateRemoteSoloTaskStatus(endpoint, status);
    }

    /// <summary>
    /// 更新一个在线子实例的状态缓存并通知 UI。
    /// </summary>
    private void UpdateRemoteSoloTaskStatus(
        InstanceEndpoint endpoint,
        SoloTaskStatusSnapshot status)
    {
        if (!Context.IsRoot)
        {
            return;
        }

        var accepted = false;
        _remoteSoloTaskStatuses.AddOrUpdate(
            endpoint.ProcessId,
            _ =>
            {
                accepted = true;
                return status;
            },
            (_, current) =>
            {
                if (status.Version < current.Version)
                {
                    return current;
                }

                accepted = true;
                return status;
            });
        if (accepted)
        {
            NotifySoloTaskStatusesChanged();
        }
    }

    /// <summary>
    /// 连接集合变化后从所有在线子实例重新查询状态，用于首次连接和重连校正。
    /// </summary>
    private async Task RefreshRemoteSoloTaskStatusesAsync()
    {
        if (!Context.IsRoot || _lifetimeCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        var targets = GetConnectedChildInstances();
        foreach (var endpoint in targets)
        {
            try
            {
                var target = GetRequiredChildConnection(endpoint);
                var response = await target.Connection.SendRequestAsync(
                    InstanceOperations.TaskStatusGet,
                    null,
                    RequestTimeout,
                    _lifetimeCancellationTokenSource.Token).ConfigureAwait(false);
                EnsureSuccessfulResponse(response);
                var status = response.Data?.ToObject<SoloTaskStatusSnapshot>(InstanceIpcProtocol.Serializer)
                             ?? SoloTaskStatusSnapshot.Idle();
                UpdateRemoteSoloTaskStatus(endpoint, status);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                              or IOException
                                              or TimeoutException
                                              or OperationCanceledException)
            {
                _logger.LogDebug(exception, "查询子实例 {ProcessId} 的独立任务状态失败", endpoint.ProcessId);
            }
        }

        var onlineProcessIds = targets.Select(x => x.ProcessId).ToHashSet();
        foreach (var processId in _remoteSoloTaskStatuses.Keys)
        {
            if (!onlineProcessIds.Contains(processId))
            {
                _remoteSoloTaskStatuses.TryRemove(processId, out _);
            }
        }

        NotifySoloTaskStatusesChanged();
    }

    /// <summary>
    /// 安全通知在线子实例集合变化，避免 UI 订阅方异常影响 IPC 登记流程。
    /// </summary>
    private void NotifyConnectedInstancesChanged()
    {
        try
        {
            ConnectedInstancesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "通知在线子实例集合变化失败");
        }

        if (Context.IsRoot)
        {
            _ = Task.Run(RefreshRemoteSoloTaskStatusesAsync);
        }
    }

    /// <summary>
    /// 安全通知独立任务状态变化，订阅方自行切回 UI 线程。
    /// </summary>
    private void NotifySoloTaskStatusesChanged()
    {
        try
        {
            SoloTaskStatusesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "通知独立任务状态变化失败");
        }
    }

    /// <summary>
    /// 判断端点是否表示当前进程。
    /// </summary>
    private bool IsCurrentInstance(InstanceEndpoint endpoint)
    {
        return endpoint.ProcessId == Context.ProcessId
               && endpoint.InstanceType == Context.InstanceType;
    }

    private static void RequestApplicationShutdown()
    {
        var application = Application.Current;
        if (application is null)
        {
            Environment.Exit(0);
            return;
        }

        _ = application.Dispatcher.BeginInvoke(new Action(application.Shutdown));
    }

    /// <summary>
    /// 在关闭请求的成功响应已经写回根实例后，关闭当前子实例应用。
    /// </summary>
    internal static void RequestApplicationShutdownAfterResponse()
    {
        RequestApplicationShutdown();
    }

    private static void EnsureSuccessfulResponse(InstanceIpcEnvelope response)
    {
        if (response.Success == true)
        {
            return;
        }

        throw new InvalidOperationException(
            response.ErrorMessage ?? response.ErrorCode ?? "实例 IPC 请求失败。");
    }

    private static async Task AwaitBackgroundTaskAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException
                                          or OperationCanceledException
                                          or ObjectDisposedException)
        {
            // HostedService 停止期间的正常清理。
        }
    }
}

public sealed class WebViewMessageReceivedEventArgs(
    int sourceProcessId,
    string operation,
    JToken? data) : EventArgs
{
    public int SourceProcessId { get; } = sourceProcessId;

    public string Operation { get; } = operation;

    public JToken? Data { get; } = data;
}
