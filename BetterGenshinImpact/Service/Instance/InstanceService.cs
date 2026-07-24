using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Instance;

public sealed class InstanceService : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PendingLaunchLifetime = TimeSpan.FromSeconds(30);

    private readonly InstanceBootstrap _bootstrap;
    private readonly IServiceProvider _serviceProvider;
    private readonly RawInputMonitor _rawInputMonitor;
    private readonly ILogger<InstanceService> _logger;
    private readonly CancellationTokenSource _lifetimeCancellationTokenSource = new();
    private readonly ConcurrentDictionary<Guid, ChildInstanceConnection> _children = new();
    private readonly ConcurrentDictionary<Guid, PendingChildLaunch> _pendingChildLaunches = new();
    private readonly ConcurrentDictionary<Guid, BetterGiInstanceType> _knownChildTypes = new();
    private readonly ConcurrentDictionary<Guid, InstanceIpcEnvelope> _activationResponses = new();
    private readonly ConcurrentQueue<string[]> _pendingActivations = new();
    private readonly ConcurrentDictionary<Guid, InstanceConnection> _relativeMouseTargets = new();
    private readonly object _parentConnectionLock = new();
    private readonly object _relativeMouseSubscriptionLock = new();

    private Task? _acceptLoopTask;
    private Task? _parentConnectionLoopTask;
    private InstanceConnection? _parentConnection;
    private IDisposable? _rawInputSubscription;
    private bool _applicationReady;
    private bool _parentRelativeMouseRequested;
    private bool _relativeMouseFocusAllowed;
    private long _lastFocusCheckTimestamp;
    private int _focusCheckPending;
    private int _stopStarted;

    public InstanceService(
        InstanceBootstrap bootstrap,
        IServiceProvider serviceProvider,
        RawInputMonitor rawInputMonitor,
        ILogger<InstanceService> logger)
    {
        _bootstrap = bootstrap;
        _serviceProvider = serviceProvider;
        _rawInputMonitor = rawInputMonitor;
        _logger = logger;
    }

    public InstanceContext Context => _bootstrap.Context;

    internal event EventHandler<RelativeMouseMoveEventArgs>? RelativeMouseReceived;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var firstServer = _bootstrap.TakeFirstServer();
        _acceptLoopTask = AcceptLoopAsync(firstServer, _lifetimeCancellationTokenSource.Token);
        if (Context.ParentInstanceId is not null
            && !string.IsNullOrWhiteSpace(Context.ParentPipeName))
        {
            _parentConnectionLoopTask = ParentConnectionLoopAsync(
                _lifetimeCancellationTokenSource.Token);
        }

        _logger.LogInformation(
            "实例 IPC 已启动：{InstanceType} {InstanceId}，管道 {PipeName}",
            Context.InstanceType,
            Context.InstanceId,
            Context.PipeName);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopStarted, 1) != 0)
        {
            return;
        }

        InstanceConnection? parentConnection;
        lock (_parentConnectionLock)
        {
            parentConnection = _parentConnection;
            _parentConnection = null;
        }

        if (parentConnection is not null)
        {
            try
            {
                await parentConnection.SendRequestAsync(
                    InstanceOperations.InstanceUnregister,
                    Context.InstanceId,
                    null,
                    TimeSpan.FromSeconds(1),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                                              or TimeoutException
                                              or OperationCanceledException)
            {
                _logger.LogDebug(exception, "向父实例发送注销消息失败");
            }

            await parentConnection.DisposeAsync().ConfigureAwait(false);
        }

        _lifetimeCancellationTokenSource.Cancel();
        StopRelativeMouseForwarding();

        var connections = _children.Values.Select(x => x.Connection).Distinct().ToArray();
        foreach (var connection in connections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        await AwaitBackgroundTaskAsync(_acceptLoopTask).ConfigureAwait(false);
        await AwaitBackgroundTaskAsync(_parentConnectionLoopTask).ConfigureAwait(false);
    }

    public InstanceLaunchInfo BeginChildLaunch(BetterGiInstanceType instanceType)
    {
        RemoveExpiredPendingLaunches();
        if (!CanCreate(instanceType))
        {
            throw new InvalidOperationException(
                $"{Context.InstanceType} 实例不能创建 {instanceType} 子实例。");
        }

        if (instanceType == BetterGiInstanceType.ChildSession
            && (_children.Values.Any(x => x.Descriptor.InstanceType == BetterGiInstanceType.ChildSession)
                || _pendingChildLaunches.Values.Any(x =>
                    x.InstanceType == BetterGiInstanceType.ChildSession)))
        {
            throw new InvalidOperationException("当前实例已经存在桌面分身 BetterGI。");
        }

        var launchInfo = new InstanceLaunchInfo(
            Guid.NewGuid(),
            instanceType,
            Context.InstanceId,
            Context.PipeName);
        _pendingChildLaunches[launchInfo.InstanceId] = new PendingChildLaunch(
            instanceType,
            DateTimeOffset.UtcNow);
        return launchInfo;
    }

    public void CancelPendingChildLaunch(Guid instanceId)
    {
        _pendingChildLaunches.TryRemove(instanceId, out _);
    }

    public void MarkApplicationReady()
    {
        _applicationReady = true;
        while (_pendingActivations.TryDequeue(out var args))
        {
            DispatchActivation(args);
        }
    }

    internal async Task SubscribeParentRelativeMouseAsync(CancellationToken cancellationToken)
    {
        if (Context.InstanceType != BetterGiInstanceType.ChildSession)
        {
            throw new InvalidOperationException("只有 ChildSession 实例可以订阅父实例的相对鼠标数据。");
        }

        _parentRelativeMouseRequested = true;
        var connection = await WaitForParentConnectionAsync(cancellationToken).ConfigureAwait(false);
        var response = await connection.SendRequestAsync(
            InstanceOperations.RelativeMouseSubscribe,
            Context.InstanceId,
            null,
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
    }

    internal async Task UnsubscribeParentRelativeMouseAsync(CancellationToken cancellationToken)
    {
        _parentRelativeMouseRequested = false;
        InstanceConnection? connection;
        lock (_parentConnectionLock)
        {
            connection = _parentConnection;
        }

        if (connection is null)
        {
            return;
        }

        var response = await connection.SendRequestAsync(
            InstanceOperations.RelativeMouseUnsubscribe,
            Context.InstanceId,
            null,
            RequestTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(response);
    }

    internal async Task<InstanceIpcEnvelope?> HandleRequestAsync(
        InstanceConnection connection,
        InstanceIpcEnvelope request,
        CancellationToken cancellationToken)
    {
        try
        {
            return request.Operation switch
            {
                InstanceOperations.Ping => InstanceIpcEnvelope.Response(
                    request,
                    Context.InstanceId,
                    Context.ToDescriptor()),
                InstanceOperations.ActivationForward => HandleActivationForward(request),
                InstanceOperations.InstanceRegister => HandleInstanceRegister(connection, request),
                InstanceOperations.InstanceUnregister => HandleInstanceUnregister(connection, request),
                InstanceOperations.InstanceHeartbeat => InstanceIpcEnvelope.Response(
                    request,
                    Context.InstanceId),
                InstanceOperations.InstanceGetTree => InstanceIpcEnvelope.Response(
                    request,
                    Context.InstanceId,
                    await BuildInstanceTreeAsync(cancellationToken).ConfigureAwait(false)),
                InstanceOperations.RelativeMouseSubscribe => HandleRelativeMouseSubscribe(
                    connection,
                    request),
                InstanceOperations.RelativeMouseUnsubscribe => HandleRelativeMouseUnsubscribe(
                    connection,
                    request),
                _ => InstanceIpcEnvelope.Failure(
                    request,
                    Context.InstanceId,
                    "unsupported_operation",
                    $"不支持的实例 IPC 操作：{request.Operation}")
            };

            // TODO: 多实例独立任务入口预留。
            // 后续在此增加目标实例选择、任务下发与状态回传。
            // 当前版本不注册任何 task.* 操作。
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or JsonException)
        {
            _logger.LogWarning(exception, "处理实例 IPC 请求失败：{Operation}", request.Operation);
            return InstanceIpcEnvelope.Failure(
                request,
                Context.InstanceId,
                "invalid_request",
                exception.GetBaseException().Message);
        }
    }

    internal void ReceiveRelativeMouseBatch(
        ulong firstSequence,
        IReadOnlyList<RelativeMouseSample> samples)
    {
        _logger.LogTrace(
            "收到相对鼠标批次：序号 {FirstSequence}，样本数 {SampleCount}",
            firstSequence,
            samples.Count);
        foreach (var sample in samples)
        {
            RelativeMouseReceived?.Invoke(
                this,
                new RelativeMouseMoveEventArgs(
                    sample.DeltaX,
                    sample.DeltaY,
                    sample.Timestamp));
        }
    }

    internal void ConnectionClosed(InstanceConnection connection)
    {
        if (connection.RemoteDescriptor is { } descriptor)
        {
            if (_children.TryGetValue(descriptor.InstanceId, out var child)
                && ReferenceEquals(child.Connection, connection))
            {
                _children.TryRemove(descriptor.InstanceId, out _);
                _relativeMouseTargets.TryRemove(descriptor.InstanceId, out _);
                StopRelativeMouseForwardingIfUnused();
                _logger.LogInformation(
                    "子实例已断开：{InstanceType} {InstanceId}",
                    descriptor.InstanceType,
                    descriptor.InstanceId);
            }
        }

        lock (_parentConnectionLock)
        {
            if (ReferenceEquals(_parentConnection, connection))
            {
                _parentConnection = null;
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
                    Context.PipeName,
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
                _logger.LogError(exception, "实例命名管道监听异常终止");
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

    private async Task ParentConnectionLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            InstanceConnection? connection = null;
            try
            {
                var client = new NamedPipeClientStream(
                    ".",
                    Context.ParentPipeName!,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough);
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                connection = new InstanceConnection(client, this, _logger);
                connection.Start(cancellationToken);

                var registerResponse = await connection.SendRequestAsync(
                    InstanceOperations.InstanceRegister,
                    Context.InstanceId,
                    new InstanceRegisterRequest
                    {
                        ParentInstanceId = Context.ParentInstanceId!.Value,
                        Descriptor = Context.ToDescriptor()
                    },
                    RequestTimeout,
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccessfulResponse(registerResponse);

                lock (_parentConnectionLock)
                {
                    _parentConnection = connection;
                }

                _logger.LogInformation(
                    "已连接父实例 {ParentInstanceId}，管道 {ParentPipeName}",
                    Context.ParentInstanceId,
                    Context.ParentPipeName);

                if (_parentRelativeMouseRequested)
                {
                    var subscribeResponse = await connection.SendRequestAsync(
                        InstanceOperations.RelativeMouseSubscribe,
                        Context.InstanceId,
                        null,
                        RequestTimeout,
                        cancellationToken).ConfigureAwait(false);
                    EnsureSuccessfulResponse(subscribeResponse);
                }

                while (!connection.Completion.IsCompleted
                       && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                    var heartbeatResponse = await connection.SendRequestAsync(
                        InstanceOperations.InstanceHeartbeat,
                        Context.InstanceId,
                        null,
                        RequestTimeout,
                        cancellationToken).ConfigureAwait(false);
                    EnsureSuccessfulResponse(heartbeatResponse);
                }

                await connection.Completion.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                                              or TimeoutException
                                              or OperationCanceledException
                                              or InvalidOperationException)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        exception,
                        "连接父实例失败，稍后重试：{ParentPipeName}",
                        Context.ParentPipeName);
                }
            }
            finally
            {
                lock (_parentConnectionLock)
                {
                    if (ReferenceEquals(_parentConnection, connection))
                    {
                        _parentConnection = null;
                    }
                }

                if (connection is not null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private InstanceIpcEnvelope HandleActivationForward(InstanceIpcEnvelope request)
    {
        if (_activationResponses.TryGetValue(request.RequestId, out var cachedResponse))
        {
            return cachedResponse;
        }

        var activation = request.Data?.ToObject<ActivationForwardRequest>(InstanceIpcProtocol.Serializer)
                         ?? throw new ArgumentException("激活请求缺少命令行参数。");
        EnqueueActivation(activation.Arguments);
        var response = InstanceIpcEnvelope.Response(request, Context.InstanceId);
        _activationResponses.TryAdd(request.RequestId, response);
        if (_activationResponses.Count > 512)
        {
            _activationResponses.Clear();
            _activationResponses.TryAdd(request.RequestId, response);
        }
        return response;
    }

    private InstanceIpcEnvelope HandleInstanceRegister(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        var registration = request.Data?.ToObject<InstanceRegisterRequest>(InstanceIpcProtocol.Serializer)
                           ?? throw new ArgumentException("实例注册请求缺少数据。");
        if (registration.ParentInstanceId != Context.InstanceId)
        {
            throw new InvalidOperationException("实例注册请求的父实例 ID 不匹配。");
        }
        if (registration.Descriptor.InstanceId != request.SourceInstanceId)
        {
            throw new InvalidOperationException("实例注册请求的来源 ID 不匹配。");
        }
        if (registration.Descriptor.InstanceId == Guid.Empty
            || registration.Descriptor.ParentInstanceId != Context.InstanceId
            || string.IsNullOrWhiteSpace(registration.Descriptor.PipeName))
        {
            throw new InvalidOperationException("实例注册请求的实例描述无效。");
        }
        var isPendingLaunch = _pendingChildLaunches.TryGetValue(
            registration.Descriptor.InstanceId,
            out var pendingLaunch)
            && pendingLaunch.InstanceType == registration.Descriptor.InstanceType;
        var isKnownChild = _knownChildTypes.TryGetValue(
            registration.Descriptor.InstanceId,
            out var knownType)
            && knownType == registration.Descriptor.InstanceType;
        if (!isPendingLaunch && !isKnownChild)
        {
            throw new InvalidOperationException("实例注册请求未对应当前实例发起的子实例启动。");
        }
        if (!CanCreate(registration.Descriptor.InstanceType))
        {
            throw new InvalidOperationException(
                $"{Context.InstanceType} 实例不能接受 {registration.Descriptor.InstanceType} 子实例。");
        }
        if (registration.Descriptor.InstanceType == BetterGiInstanceType.ChildSession
            && _children.Values.Any(x =>
                x.Descriptor.InstanceType == BetterGiInstanceType.ChildSession
                && x.Descriptor.InstanceId != registration.Descriptor.InstanceId))
        {
            throw new InvalidOperationException("当前实例已经注册了一个 ChildSession 子实例。");
        }

        connection.RemoteDescriptor = registration.Descriptor;
        var child = new ChildInstanceConnection(registration.Descriptor, connection);
        if (_children.TryGetValue(registration.Descriptor.InstanceId, out var existing)
            && !ReferenceEquals(existing.Connection, connection))
        {
            _ = existing.Connection.DisposeAsync().AsTask();
        }
        _children[registration.Descriptor.InstanceId] = child;
        _knownChildTypes[registration.Descriptor.InstanceId] =
            registration.Descriptor.InstanceType;
        _pendingChildLaunches.TryRemove(registration.Descriptor.InstanceId, out _);

        _logger.LogInformation(
            "子实例已注册：{InstanceType} {InstanceId}，管道 {PipeName}",
            registration.Descriptor.InstanceType,
            registration.Descriptor.InstanceId,
            registration.Descriptor.PipeName);
        return InstanceIpcEnvelope.Response(request, Context.InstanceId);
    }

    private InstanceIpcEnvelope HandleInstanceUnregister(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        if (connection.RemoteDescriptor is { } descriptor)
        {
            _children.TryRemove(descriptor.InstanceId, out _);
            _knownChildTypes.TryRemove(descriptor.InstanceId, out _);
            _relativeMouseTargets.TryRemove(descriptor.InstanceId, out _);
            StopRelativeMouseForwardingIfUnused();
        }
        return InstanceIpcEnvelope.Response(request, Context.InstanceId);
    }

    private InstanceIpcEnvelope HandleRelativeMouseSubscribe(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        var descriptor = connection.RemoteDescriptor
                         ?? throw new InvalidOperationException("相对鼠标订阅方尚未注册为子实例。");
        if (descriptor.InstanceType != BetterGiInstanceType.ChildSession)
        {
            throw new InvalidOperationException("只有 ChildSession 子实例可以订阅相对鼠标数据。");
        }

        _relativeMouseTargets[descriptor.InstanceId] = connection;
        StartRelativeMouseForwarding();
        return InstanceIpcEnvelope.Response(
            request,
            Context.InstanceId,
            new RelativeMouseState { IsSubscribed = true });
    }

    private InstanceIpcEnvelope HandleRelativeMouseUnsubscribe(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        if (connection.RemoteDescriptor is { } descriptor)
        {
            _relativeMouseTargets.TryRemove(descriptor.InstanceId, out _);
            StopRelativeMouseForwardingIfUnused();
        }
        return InstanceIpcEnvelope.Response(
            request,
            Context.InstanceId,
            new RelativeMouseState { IsSubscribed = false });
    }

    private async Task<InstanceTreeNode> BuildInstanceTreeAsync(CancellationToken cancellationToken)
    {
        var children = new List<InstanceTreeNode>();
        foreach (var child in _children.Values.ToArray())
        {
            try
            {
                var response = await child.Connection.SendRequestAsync(
                    InstanceOperations.InstanceGetTree,
                    Context.InstanceId,
                    null,
                    TimeSpan.FromSeconds(2),
                    cancellationToken).ConfigureAwait(false);
                if (response.Success == true
                    && response.Data?.ToObject<InstanceTreeNode>(InstanceIpcProtocol.Serializer) is { } tree)
                {
                    children.Add(tree);
                    continue;
                }
            }
            catch (Exception exception) when (exception is IOException
                                              or TimeoutException
                                              or OperationCanceledException)
            {
                _logger.LogDebug(
                    exception,
                    "读取子实例树失败：{InstanceId}",
                    child.Descriptor.InstanceId);
            }

            children.Add(new InstanceTreeNode { Instance = child.Descriptor });
        }

        return new InstanceTreeNode
        {
            Instance = Context.ToDescriptor(),
            Children = children.ToArray()
        };
    }

    private bool CanCreate(BetterGiInstanceType childType)
    {
        return Context.InstanceType switch
        {
            BetterGiInstanceType.Primary => childType is BetterGiInstanceType.ChildSession
                or BetterGiInstanceType.WebView,
            BetterGiInstanceType.ChildSession => childType == BetterGiInstanceType.WebView,
            _ => false
        };
    }

    private void EnqueueActivation(string[] args)
    {
        if (!_applicationReady)
        {
            _pendingActivations.Enqueue(args);
            return;
        }
        DispatchActivation(args);
    }

    private void DispatchActivation(string[] args)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            var mainWindow = Application.Current.MainWindow;
            mainWindow?.Show();
            mainWindow?.Activate();
            if (mainWindow is not null)
            {
                SystemControl.RestoreWindow(new WindowInteropHelper(mainWindow).Handle);
            }

            var commandLineOptions = CommandLineOptions.Parse(args);
            App.GetService<HomePageViewModel>()?.HandleActivation(commandLineOptions);
        }));
    }

    private async Task<InstanceConnection> WaitForParentConnectionAsync(
        CancellationToken cancellationToken)
    {
        var timeoutAt = DateTime.UtcNow + RequestTimeout;
        while (DateTime.UtcNow < timeoutAt)
        {
            lock (_parentConnectionLock)
            {
                if (_parentConnection is not null)
                {
                    return _parentConnection;
                }
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("尚未连接父 BetterGI 实例。");
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

    private void StartRelativeMouseForwarding()
    {
        lock (_relativeMouseSubscriptionLock)
        {
            _rawInputSubscription ??= _rawInputMonitor.Subscribe(OnRelativeMouseMoved);
        }
        RequestRelativeMouseFocusRefresh(force: true);
    }

    private void StopRelativeMouseForwardingIfUnused()
    {
        if (!_relativeMouseTargets.IsEmpty)
        {
            return;
        }
        StopRelativeMouseForwarding();
    }

    private void StopRelativeMouseForwarding()
    {
        lock (_relativeMouseSubscriptionLock)
        {
            _rawInputSubscription?.Dispose();
            _rawInputSubscription = null;
        }
        _relativeMouseFocusAllowed = false;
    }

    private void OnRelativeMouseMoved(object? sender, RelativeMouseMoveEventArgs eventArgs)
    {
        RequestRelativeMouseFocusRefresh(force: false);
        if (!_relativeMouseFocusAllowed)
        {
            return;
        }

        foreach (var connection in _relativeMouseTargets.Values)
        {
            connection.EnqueueRelativeMouse(eventArgs);
        }
    }

    private void RequestRelativeMouseFocusRefresh(bool force)
    {
        var now = Stopwatch.GetTimestamp();
        if (!force
            && Stopwatch.GetElapsedTime(
                Interlocked.Read(ref _lastFocusCheckTimestamp),
                now) < TimeSpan.FromMilliseconds(100))
        {
            return;
        }
        Interlocked.Exchange(ref _lastFocusCheckTimestamp, now);
        if (Interlocked.Exchange(ref _focusCheckPending, 1) != 0)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            Interlocked.Exchange(ref _focusCheckPending, 0);
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                _relativeMouseFocusAllowed =
                    _serviceProvider.GetService<ChildSessionService>()
                        ?.IsRelativeMouseForwardingAvailable() == true;
            }
            finally
            {
                Interlocked.Exchange(ref _focusCheckPending, 0);
            }
        }));
    }

    private void RemoveExpiredPendingLaunches()
    {
        var expiresBefore = DateTimeOffset.UtcNow - PendingLaunchLifetime;
        foreach (var pending in _pendingChildLaunches)
        {
            if (pending.Value.CreatedAt < expiresBefore)
            {
                _pendingChildLaunches.TryRemove(pending.Key, out _);
            }
        }
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

    private sealed record ChildInstanceConnection(
        InstanceDescriptor Descriptor,
        InstanceConnection Connection);

    private sealed record PendingChildLaunch(
        BetterGiInstanceType InstanceType,
        DateTimeOffset CreatedAt);
}

internal sealed class InstanceRegisterRequest
{
    public Guid ParentInstanceId { get; init; }

    public InstanceDescriptor Descriptor { get; init; } = new();
}

internal sealed class RelativeMouseState
{
    public bool IsSubscribed { get; init; }
}
