using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Instance.MessageHandlers;

/// <summary>
/// 负责分发和处理 JSON 格式的实例 IPC 请求。
/// 连接建立、重连与关闭仍由 <see cref="InstanceService"/> 编排，本类只处理消息语义。
/// </summary>
internal sealed class InstanceRequestHandler
{
    private static readonly TimeSpan ForwardRequestTimeout = TimeSpan.FromSeconds(5);

    private readonly InstanceContext _context;
    private readonly InstanceMessageState _state;
    private readonly RelativeMouseMessageHandler _relativeMouseMessageHandler;
    private readonly Action<string[]> _enqueueActivation;
    private readonly Action<WebViewMessage> _dispatchWebViewMessage;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, InstanceIpcEnvelope> _activationResponses = new();

    internal InstanceRequestHandler(
        InstanceContext context,
        InstanceMessageState state,
        RelativeMouseMessageHandler relativeMouseMessageHandler,
        Action<string[]> enqueueActivation,
        Action<WebViewMessage> dispatchWebViewMessage,
        ILogger logger)
    {
        _context = context;
        _state = state;
        _relativeMouseMessageHandler = relativeMouseMessageHandler;
        _enqueueActivation = enqueueActivation;
        _dispatchWebViewMessage = dispatchWebViewMessage;
        _logger = logger;
    }

    /// <summary>
    /// 将请求路由到对应处理方法，并把可预期的请求错误转换为失败响应。
    /// </summary>
    internal async Task<InstanceIpcEnvelope?> HandleAsync(
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
                    _context.ToEndpoint()),
                InstanceOperations.ConnectionOpen =>
                    await HandleConnectionOpenAsync(
                        connection,
                        request,
                        cancellationToken).ConfigureAwait(false),
                InstanceOperations.ActivationDispatch => HandleActivationDispatch(
                    connection,
                    request),
                InstanceOperations.RelativeMouseSubscribe =>
                    _relativeMouseMessageHandler.HandleSubscribe(connection, request),
                InstanceOperations.RelativeMouseUnsubscribe =>
                    _relativeMouseMessageHandler.HandleUnsubscribe(connection, request),
                InstanceOperations.WebViewList => HandleWebViewList(connection, request),
                InstanceOperations.WebViewSend =>
                    await HandleWebViewSendAsync(
                        connection,
                        request,
                        cancellationToken).ConfigureAwait(false),
                InstanceOperations.WebViewMessage => HandleWebViewMessage(connection, request),
                _ => InstanceIpcEnvelope.Failure(
                    request,
                    "unsupported_operation",
                    $"不支持的实例 IPC 操作：{request.Operation}")
            };

            // TODO: 多实例独立任务入口预留。
            // 后续在此增加目标实例选择、任务下发与状态回传。
            // 当前版本不注册任何 task.* 操作。
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or IOException
                                          or TimeoutException
                                          or JsonException)
        {
            _logger.LogWarning(exception, "处理实例 IPC 请求失败：{Operation}", request.Operation);
            return InstanceIpcEnvelope.Failure(
                request,
                "invalid_request",
                exception.GetBaseException().Message);
        }
    }

    /// <summary>
    /// 激活消息按 RequestId 去重，避免管道重试导致主窗口被重复激活。
    /// </summary>
    private InstanceIpcEnvelope HandleActivationDispatch(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        if (_context.InstanceType == BetterGiInstanceType.Primary
            || connection.RemoteEndpoint?.InstanceType != BetterGiInstanceType.Primary)
        {
            throw new InvalidOperationException("只有根实例可以向 BetterGI 客户端分发激活消息。");
        }

        if (_activationResponses.TryGetValue(request.RequestId, out var cachedResponse))
        {
            return cachedResponse;
        }

        var activation =
            request.Data?.ToObject<ActivationDispatchRequest>(InstanceIpcProtocol.Serializer)
            ?? throw new ArgumentException("激活请求缺少命令行参数。");
        ActivationForwardingPolicy.ThrowIfManagedAutomation(activation.Arguments);
        _enqueueActivation(activation.Arguments);
        return CacheActivationResponse(
            request.RequestId,
            InstanceIpcEnvelope.Response(request));
    }

    /// <summary>
    /// 校验子实例身份和启动记录后，将当前连接登记为有效子连接。
    /// v2 不再校验父实例 ID 或启动记录，而是使用根管道客户端的真实 PID 和 Session。
    /// </summary>
    private async Task<InstanceIpcEnvelope> HandleConnectionOpenAsync(
        InstanceConnection connection,
        InstanceIpcEnvelope request,
        CancellationToken cancellationToken)
    {
        if (_context.InstanceType != BetterGiInstanceType.Primary)
        {
            throw new InvalidOperationException("只有根实例可以接受客户端连接登记。");
        }
        if (connection.RemoteEndpoint is not null)
        {
            throw new InvalidOperationException("当前管道连接已经完成登记。");
        }
        if (connection.ClientProcessId is not { } processId
            || connection.ClientSessionId is not { } sessionId)
        {
            throw new InvalidOperationException("无法取得命名管道客户端的进程或 Session 信息。");
        }

        var open =
            request.Data?.ToObject<ConnectionOpenRequest>(InstanceIpcProtocol.Serializer)
            ?? throw new ArgumentException("连接登记请求缺少数据。");
        if (open.RequestedType == BetterGiInstanceType.WebView)
        {
            var endpoint = CreateEndpoint(
                BetterGiInstanceType.WebView,
                processId,
                sessionId);
            connection.RemoteEndpoint = endpoint;
            RegisteredInstanceConnection? replaced = null;
            lock (_state.RegistrationLock)
            {
                if (_state.WebViewConnectionsByProcessId.TryGetValue(
                        processId,
                        out var existing)
                    && !ReferenceEquals(existing.Connection, connection))
                {
                    replaced = existing;
                }
                _state.WebViewConnectionsByProcessId[processId] =
                    new RegisteredInstanceConnection(endpoint, connection);
            }
            if (replaced is not null)
            {
                _ = replaced.Connection.DisposeAsync().AsTask();
            }

            _logger.LogInformation(
                "WebView 已连接根实例：进程 {ProcessId}，Session {SessionId}",
                processId,
                sessionId);
            return CreateOpenResponse(
                request,
                ConnectionOpenDisposition.Accepted,
                BetterGiInstanceType.WebView);
        }

        if (open.RequestedType == BetterGiInstanceType.ChildSession
            && sessionId == _context.WindowsSessionId)
        {
            throw new InvalidOperationException(
                "ChildSession 不能与根实例位于相同 Windows Session。");
        }

        if (sessionId == _context.WindowsSessionId)
        {
            if (_activationResponses.TryGetValue(request.RequestId, out var cachedResponse))
            {
                return cachedResponse;
            }

            ActivationForwardingPolicy.ThrowIfManagedAutomation(open.Arguments);
            _enqueueActivation(open.Arguments);
            return CacheActivationResponse(
                request.RequestId,
                CreateOpenResponse(
                    request,
                    ConnectionOpenDisposition.ActivationForwarded,
                    BetterGiInstanceType.Primary));
        }

        RegisteredInstanceConnection? duplicate;
        RegisteredInstanceConnection? replacedConnection = null;
        var childEndpoint = CreateEndpoint(
            BetterGiInstanceType.ChildSession,
            processId,
            sessionId);
        lock (_state.RegistrationLock)
        {
            _state.BetterGiConnectionsBySession.TryGetValue(sessionId, out duplicate);
            var canReplace = duplicate is null
                             || duplicate.Endpoint.ProcessId == processId
                             || open.RestartFromProcessId == duplicate.Endpoint.ProcessId;
            if (canReplace)
            {
                if (duplicate is not null
                    && !ReferenceEquals(duplicate.Connection, connection))
                {
                    replacedConnection = duplicate;
                }
                connection.RemoteEndpoint = childEndpoint;
                _state.BetterGiConnectionsBySession[sessionId] =
                    new RegisteredInstanceConnection(childEndpoint, connection);
                duplicate = null;
            }
        }

        if (duplicate is not null)
        {
            if (_activationResponses.TryGetValue(request.RequestId, out var cachedResponse))
            {
                return cachedResponse;
            }

            ActivationForwardingPolicy.ThrowIfManagedAutomation(open.Arguments);
            try
            {
                var activationResponse = await duplicate.Connection.SendRequestAsync(
                    InstanceOperations.ActivationDispatch,
                    new ActivationDispatchRequest { Arguments = open.Arguments },
                    ForwardRequestTimeout,
                    cancellationToken).ConfigureAwait(false);
                EnsureSuccessfulResponse(activationResponse);
                return CacheActivationResponse(
                    request.RequestId,
                    CreateOpenResponse(
                        request,
                        ConnectionOpenDisposition.ActivationForwarded,
                        BetterGiInstanceType.ChildSession));
            }
            catch (Exception exception) when (exception is IOException
                                              or TimeoutException
                                              or OperationCanceledException)
            {
                _logger.LogDebug(
                    exception,
                    "向 Session {SessionId} 的现有 BetterGI 转发激活失败，改为接纳新连接",
                    sessionId);
                lock (_state.RegistrationLock)
                {
                    if (_state.BetterGiConnectionsBySession.TryGetValue(
                            sessionId,
                            out var current)
                        && ReferenceEquals(current.Connection, duplicate.Connection))
                    {
                        connection.RemoteEndpoint = childEndpoint;
                        _state.BetterGiConnectionsBySession[sessionId] =
                            new RegisteredInstanceConnection(childEndpoint, connection);
                        replacedConnection = duplicate;
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Session {sessionId} 的 BetterGI 连接已发生变化。");
                    }
                }
            }
        }

        if (replacedConnection is not null)
        {
            _ = replacedConnection.Connection.DisposeAsync().AsTask();
        }

        _logger.LogInformation(
            "桌面分身 BetterGI 已连接根实例：进程 {ProcessId}，Session {SessionId}",
            processId,
            sessionId);
        return CreateOpenResponse(
            request,
            ConnectionOpenDisposition.Accepted,
            BetterGiInstanceType.ChildSession);
    }

    private InstanceIpcEnvelope HandleWebViewList(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        var requester = RequireRegisteredEndpoint(connection);
        if (requester.InstanceType == BetterGiInstanceType.WebView)
        {
            throw new InvalidOperationException("WebView 不能枚举其他 WebView。");
        }

        var endpoints = _state.WebViewConnectionsByProcessId.Values
            .Where(x => requester.InstanceType == BetterGiInstanceType.Primary
                        || x.Endpoint.WindowsSessionId == requester.WindowsSessionId)
            .Select(x => x.Endpoint)
            .OrderBy(x => x.WindowsSessionId)
            .ThenBy(x => x.ProcessId)
            .ToArray();
        return InstanceIpcEnvelope.Response(
            request,
            new WebViewListResponse { Endpoints = endpoints });
    }

    private async Task<InstanceIpcEnvelope> HandleWebViewSendAsync(
        InstanceConnection connection,
        InstanceIpcEnvelope request,
        CancellationToken cancellationToken)
    {
        var requester = RequireRegisteredEndpoint(connection);
        if (requester.InstanceType == BetterGiInstanceType.WebView)
        {
            throw new InvalidOperationException("WebView 不能通过根实例向其他 WebView 转发消息。");
        }

        var send = request.Data?.ToObject<WebViewSendRequest>(InstanceIpcProtocol.Serializer)
                   ?? throw new ArgumentException("WebView 转发请求缺少数据。");
        if (string.IsNullOrWhiteSpace(send.Operation))
        {
            throw new ArgumentException("WebView 转发请求缺少操作名称。");
        }
        if (!_state.WebViewConnectionsByProcessId.TryGetValue(
                send.TargetProcessId,
                out var target))
        {
            throw new InvalidOperationException(
                $"WebView 进程 {send.TargetProcessId} 当前不在线。");
        }
        if (requester.InstanceType == BetterGiInstanceType.ChildSession
            && target.Endpoint.WindowsSessionId != requester.WindowsSessionId)
        {
            throw new InvalidOperationException("桌面分身不能访问其他 Session 中的 WebView。");
        }

        var targetResponse = await target.Connection.SendRequestAsync(
            InstanceOperations.WebViewMessage,
            new WebViewMessage
            {
                SourceProcessId = requester.ProcessId,
                Operation = send.Operation,
                Data = send.Data
            },
            ForwardRequestTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccessfulResponse(targetResponse);
        return InstanceIpcEnvelope.Response(request);
    }

    private InstanceIpcEnvelope HandleWebViewMessage(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        if (_context.InstanceType != BetterGiInstanceType.WebView
            || connection.RemoteEndpoint?.InstanceType != BetterGiInstanceType.Primary)
        {
            throw new InvalidOperationException("只有根实例可以向 WebView 分发消息。");
        }

        var message = request.Data?.ToObject<WebViewMessage>(InstanceIpcProtocol.Serializer)
                      ?? throw new ArgumentException("WebView 消息缺少数据。");
        _dispatchWebViewMessage(message);
        return InstanceIpcEnvelope.Response(request);
    }

    private InstanceIpcEnvelope CreateOpenResponse(
        InstanceIpcEnvelope request,
        ConnectionOpenDisposition disposition,
        BetterGiInstanceType assignedType)
    {
        return InstanceIpcEnvelope.Response(
            request,
            new ConnectionOpenResponse
            {
                Disposition = disposition,
                AssignedType = assignedType,
                RootProcessId = _context.ProcessId,
                RootSessionId = _context.WindowsSessionId
            });
    }

    private static InstanceEndpoint CreateEndpoint(
        BetterGiInstanceType instanceType,
        int processId,
        int sessionId)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            using var process = Process.GetProcessById(processId);
            startedAt = new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or InvalidOperationException
                                          or System.ComponentModel.Win32Exception)
        {
            // 连接已经证明进程存在；读取启动时间失败不影响连接登记。
        }

        return new InstanceEndpoint
        {
            InstanceType = instanceType,
            ProcessId = processId,
            WindowsSessionId = sessionId,
            StartedAt = startedAt
        };
    }

    private static InstanceEndpoint RequireRegisteredEndpoint(InstanceConnection connection)
    {
        return connection.RemoteEndpoint
               ?? throw new InvalidOperationException("当前管道连接尚未完成登记。");
    }

    private InstanceIpcEnvelope CacheActivationResponse(
        Guid requestId,
        InstanceIpcEnvelope response)
    {
        _activationResponses.TryAdd(requestId, response);
        if (_activationResponses.Count > 512)
        {
            _activationResponses.Clear();
            _activationResponses.TryAdd(requestId, response);
        }
        return response;
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
}

internal static class ActivationForwardingPolicy
{
    internal static void ThrowIfManagedAutomation(string[] arguments)
    {
        if (CommandLineOptions.Parse(arguments).ShouldDeferGameStart)
        {
            throw new InvalidOperationException(
                "已有 BetterGI 实例时无法转发托管自动化任务，请先退出现有实例后重试。");
        }
    }
}
