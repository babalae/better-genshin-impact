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
using BetterGenshinImpact.Service.Instance.MessageHandlers;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Instance;

public sealed class InstanceService : IHostedService, IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PendingLaunchLifetime = TimeSpan.FromSeconds(30);

    private readonly InstanceBootstrap _bootstrap;
    private readonly ILogger<InstanceService> _logger;
    private readonly InstanceMessageState _messageState = new();
    private readonly InstanceRequestHandler _requestHandler;
    private readonly RelativeMouseMessageHandler _relativeMouseMessageHandler;
    private readonly CancellationTokenSource _lifetimeCancellationTokenSource = new();
    private readonly ConcurrentQueue<string[]> _pendingActivations = new();
    private readonly object _parentConnectionLock = new();

    private Task? _acceptLoopTask;
    private Task? _parentConnectionLoopTask;
    private InstanceConnection? _parentConnection;
    private bool _applicationReady;
    private int _stopStarted;

    public InstanceService(
        InstanceBootstrap bootstrap,
        IServiceProvider serviceProvider,
        RawInputMonitor rawInputMonitor,
        ILogger<InstanceService> logger)
    {
        _bootstrap = bootstrap;
        _logger = logger;
        _relativeMouseMessageHandler = new RelativeMouseMessageHandler(
            Context,
            serviceProvider,
            rawInputMonitor,
            IsParentConnection,
            logger);
        _requestHandler = new InstanceRequestHandler(
            Context,
            _messageState,
            _relativeMouseMessageHandler,
            CanCreate,
            EnqueueActivation,
            logger);
    }

    public InstanceContext Context => _bootstrap.Context;

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
        _relativeMouseMessageHandler.Stop();

        var connections = _messageState.Children.Values
            .Select(x => x.Connection)
            .Distinct()
            .ToArray();
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
            && (_messageState.Children.Values.Any(
                    x => x.Descriptor.InstanceType == BetterGiInstanceType.ChildSession)
                || _messageState.PendingChildLaunches.Values.Any(x =>
                    x.InstanceType == BetterGiInstanceType.ChildSession)))
        {
            throw new InvalidOperationException("当前实例已经存在桌面分身 BetterGI。");
        }

        var launchInfo = new InstanceLaunchInfo(
            Guid.NewGuid(),
            instanceType,
            Context.InstanceId,
            Context.PipeName);
        _messageState.PendingChildLaunches[launchInfo.InstanceId] = new PendingChildLaunch(
            instanceType,
            DateTimeOffset.UtcNow);
        return launchInfo;
    }

    public void CancelPendingChildLaunch(Guid instanceId)
    {
        _messageState.PendingChildLaunches.TryRemove(instanceId, out _);
    }

    public void MarkApplicationReady()
    {
        _applicationReady = true;
        while (_pendingActivations.TryDequeue(out var args))
        {
            DispatchActivation(args);
        }
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

    private bool IsParentConnection(InstanceConnection connection)
    {
        lock (_parentConnectionLock)
        {
            return ReferenceEquals(_parentConnection, connection);
        }
    }

    internal void ConnectionClosed(InstanceConnection connection)
    {
        if (connection.RemoteDescriptor is { } descriptor)
        {
            if (_messageState.Children.TryGetValue(descriptor.InstanceId, out var child)
                && ReferenceEquals(child.Connection, connection))
            {
                _messageState.Children.TryRemove(descriptor.InstanceId, out _);
                _relativeMouseMessageHandler.RemoveTarget(descriptor.InstanceId);
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

                if (Context.InstanceType == BetterGiInstanceType.ChildSession)
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

    private static void EnsureSuccessfulResponse(InstanceIpcEnvelope response)
    {
        if (response.Success == true)
        {
            return;
        }
        throw new InvalidOperationException(
            response.ErrorMessage ?? response.ErrorCode ?? "实例 IPC 请求失败。");
    }


    private void RemoveExpiredPendingLaunches()
    {
        var expiresBefore = DateTimeOffset.UtcNow - PendingLaunchLifetime;
        foreach (var pending in _messageState.PendingChildLaunches)
        {
            if (pending.Value.CreatedAt < expiresBefore)
            {
                _messageState.PendingChildLaunches.TryRemove(pending.Key, out _);
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

}
