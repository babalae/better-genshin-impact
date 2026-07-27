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
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.ChildSession;
using BetterGenshinImpact.Service.Instance.MessageHandlers;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Service.Instance;

public sealed class InstanceService : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly InstanceBootstrap _bootstrap;
    private readonly ILogger<InstanceService> _logger;
    private readonly InstanceMessageState _messageState = new();
    private readonly InstanceRequestHandler _requestHandler;
    private readonly RelativeMouseMessageHandler _relativeMouseMessageHandler;
    private readonly CancellationTokenSource _lifetimeCancellationTokenSource = new();
    private readonly ConcurrentQueue<string[]> _pendingActivations = new();
    private readonly ConcurrentDictionary<string, byte> _startedAutomationRuns =
        new(StringComparer.Ordinal);
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
        ILogger<InstanceService> logger)
    {
        _bootstrap = bootstrap;
        _logger = logger;
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
        _requestHandler = new InstanceRequestHandler(
            Context,
            _messageState,
            _relativeMouseMessageHandler,
            EnqueueActivation,
            DispatchWebViewMessage,
            StartOneDragonTaskAsync,
            logger);
    }

    public event EventHandler<WebViewMessageReceivedEventArgs>? WebViewMessageReceived;

    public InstanceContext Context => _bootstrap.Context;

    public bool IsGameMouseModeEnabled =>
        _relativeMouseMessageHandler.IsGameMouseModeEnabled;

    public void SetGameMouseModeEnabled(bool enabled)
    {
        _relativeMouseMessageHandler.SetGameMouseModeEnabled(enabled);
    }

    public async Task<InstanceEndpoint> WaitForChildSessionAsync(
        int windowsSessionId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!Context.IsRoot)
        {
            throw new InvalidOperationException("只有根实例可以等待桌面分身注册。");
        }

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_messageState.BetterGiConnectionsBySession.TryGetValue(
                    windowsSessionId,
                    out var child)
                && child.Endpoint.InstanceType == BetterGiInstanceType.ChildSession)
            {
                return child.Endpoint;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"等待桌面分身 Session {windowsSessionId} 注册超时。");
    }

    public async Task StartOneDragonInChildAsync(
        int windowsSessionId,
        string runId,
        string configName,
        string resultPath,
        CancellationToken cancellationToken = default)
    {
        if (!Context.IsRoot)
        {
            throw new InvalidOperationException("只有根实例可以向桌面分身下发任务。");
        }
        if (!_messageState.BetterGiConnectionsBySession.TryGetValue(
                windowsSessionId,
                out var child)
            || child.Endpoint.InstanceType != BetterGiInstanceType.ChildSession)
        {
            throw new InvalidOperationException(
                $"桌面分身 Session {windowsSessionId} 当前未注册。");
        }

        var response = await child.Connection.SendRequestAsync(
            InstanceOperations.TaskStartOneDragon,
            new StartOneDragonTaskRequest
            {
                RunId = runId,
                ConfigName = configName,
                ResultPath = resultPath
            },
            TimeSpan.FromSeconds(15),
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
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
                .OrderBy(x => x.WindowsSessionId)
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
        if (Context.InstanceType == BetterGiInstanceType.Primary
            && connection.RemoteEndpoint is { } endpoint)
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
                _relativeMouseMessageHandler.RemoveTarget(endpoint.WindowsSessionId);
                _logger.LogInformation(
                    "桌面分身 BetterGI 已断开：进程 {ProcessId}，Session {SessionId}",
                    endpoint.ProcessId,
                    endpoint.WindowsSessionId);
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
                _logger.LogInformation(
                    "WebView 已断开：进程 {ProcessId}，Session {SessionId}",
                    endpoint.ProcessId,
                    endpoint.WindowsSessionId);
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
            if (commandLineOptions.Action == CommandLineAction.ChildSessionOneDragon)
            {
                _ = App.GetService<ChildSessionAutomationService>()
                    ?.StartAsync(commandLineOptions, hideRootWhenDone: false);
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

    private async Task StartOneDragonTaskAsync(StartOneDragonTaskRequest request)
    {
        if (!_startedAutomationRuns.TryAdd(request.RunId, 0))
        {
            return;
        }

        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var viewModel = App.GetService<OneDragonFlowViewModel>()
                                ?? throw new InvalidOperationException(
                                    "无法创建一条龙任务协调器。");
                _ = RunManagedAutomationAndReleaseAsync(viewModel, request);
            });
        }
        catch
        {
            _startedAutomationRuns.TryRemove(request.RunId, out _);
            throw;
        }
    }

    private async Task RunManagedAutomationAndReleaseAsync(
        OneDragonFlowViewModel viewModel,
        StartOneDragonTaskRequest request)
    {
        try
        {
            await viewModel.RunManagedAutomationAsync(
                request.ConfigName,
                request.RunId,
                request.ResultPath);
        }
        finally
        {
            _startedAutomationRuns.TryRemove(request.RunId, out _);
        }
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
