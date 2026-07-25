using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Instance.MessageHandlers;

/// <summary>
/// 负责分发和处理 JSON 格式的实例 IPC 请求。
/// 连接建立、重连与关闭仍由 <see cref="InstanceService"/> 编排，本类只处理消息语义。
/// </summary>
internal sealed class InstanceRequestHandler
{
    private static readonly TimeSpan ChildTreeRequestTimeout = TimeSpan.FromSeconds(2);

    private readonly InstanceContext _context;
    private readonly InstanceMessageState _state;
    private readonly RelativeMouseMessageHandler _relativeMouseMessageHandler;
    private readonly Func<BetterGiInstanceType, bool> _canCreate;
    private readonly Action<string[]> _enqueueActivation;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, InstanceIpcEnvelope> _activationResponses = new();

    internal InstanceRequestHandler(
        InstanceContext context,
        InstanceMessageState state,
        RelativeMouseMessageHandler relativeMouseMessageHandler,
        Func<BetterGiInstanceType, bool> canCreate,
        Action<string[]> enqueueActivation,
        ILogger logger)
    {
        _context = context;
        _state = state;
        _relativeMouseMessageHandler = relativeMouseMessageHandler;
        _canCreate = canCreate;
        _enqueueActivation = enqueueActivation;
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
                    _context.InstanceId,
                    _context.ToDescriptor()),
                InstanceOperations.ActivationForward => HandleActivationForward(request),
                InstanceOperations.InstanceRegister => HandleInstanceRegister(connection, request),
                InstanceOperations.InstanceUnregister => HandleInstanceUnregister(connection, request),
                InstanceOperations.InstanceHeartbeat => InstanceIpcEnvelope.Response(
                    request,
                    _context.InstanceId),
                InstanceOperations.InstanceGetTree => InstanceIpcEnvelope.Response(
                    request,
                    _context.InstanceId,
                    await BuildInstanceTreeAsync(connection, cancellationToken).ConfigureAwait(false)),
                InstanceOperations.RelativeMouseSubscribe =>
                    _relativeMouseMessageHandler.HandleSubscribe(connection, request),
                InstanceOperations.RelativeMouseUnsubscribe =>
                    _relativeMouseMessageHandler.HandleUnsubscribe(connection, request),
                _ => InstanceIpcEnvelope.Failure(
                    request,
                    _context.InstanceId,
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
                _context.InstanceId,
                "invalid_request",
                exception.GetBaseException().Message);
        }
    }

    /// <summary>
    /// 激活消息按 RequestId 去重，避免管道重试导致主窗口被重复激活。
    /// </summary>
    private InstanceIpcEnvelope HandleActivationForward(InstanceIpcEnvelope request)
    {
        if (_activationResponses.TryGetValue(request.RequestId, out var cachedResponse))
        {
            return cachedResponse;
        }

        var activation = request.Data?.ToObject<ActivationForwardRequest>(InstanceIpcProtocol.Serializer)
                         ?? throw new ArgumentException("激活请求缺少命令行参数。");
        _enqueueActivation(activation.Arguments);
        var response = InstanceIpcEnvelope.Response(request, _context.InstanceId);
        _activationResponses.TryAdd(request.RequestId, response);
        if (_activationResponses.Count > 512)
        {
            _activationResponses.Clear();
            _activationResponses.TryAdd(request.RequestId, response);
        }
        return response;
    }

    /// <summary>
    /// 校验子实例身份和启动记录后，将当前连接登记为有效子连接。
    /// </summary>
    private InstanceIpcEnvelope HandleInstanceRegister(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        var registration = request.Data?.ToObject<InstanceRegisterRequest>(InstanceIpcProtocol.Serializer)
                           ?? throw new ArgumentException("实例注册请求缺少数据。");
        if (registration.ParentInstanceId != _context.InstanceId)
        {
            throw new InvalidOperationException("实例注册请求的父实例 ID 不匹配。");
        }
        if (registration.Descriptor.InstanceId != request.SourceInstanceId)
        {
            throw new InvalidOperationException("实例注册请求的来源 ID 不匹配。");
        }
        if (!InstanceIds.IsValid(registration.Descriptor.InstanceId)
            || registration.Descriptor.ParentInstanceId != _context.InstanceId
            || string.IsNullOrWhiteSpace(registration.Descriptor.PipeName))
        {
            throw new InvalidOperationException("实例注册请求的实例描述无效。");
        }

        var isPendingLaunch = _state.PendingChildLaunches.TryGetValue(
            registration.Descriptor.InstanceId,
            out var pendingLaunch)
            && pendingLaunch.InstanceType == registration.Descriptor.InstanceType;
        var isKnownChild = _state.KnownChildTypes.TryGetValue(
            registration.Descriptor.InstanceId,
            out var knownType)
            && knownType == registration.Descriptor.InstanceType;
        if (!isPendingLaunch && !isKnownChild)
        {
            throw new InvalidOperationException("实例注册请求未对应当前实例发起的子实例启动。");
        }
        if (!_canCreate(registration.Descriptor.InstanceType))
        {
            throw new InvalidOperationException(
                $"{_context.InstanceType} 实例不能接受 {registration.Descriptor.InstanceType} 子实例。");
        }
        if (registration.Descriptor.InstanceType == BetterGiInstanceType.ChildSession
            && _state.Children.Values.Any(x =>
                x.Descriptor.InstanceType == BetterGiInstanceType.ChildSession
                && x.Descriptor.InstanceId != registration.Descriptor.InstanceId))
        {
            throw new InvalidOperationException("当前实例已经注册了一个 ChildSession 子实例。");
        }

        connection.RemoteDescriptor = registration.Descriptor;
        var child = new ChildInstanceConnection(registration.Descriptor, connection);
        if (_state.Children.TryGetValue(registration.Descriptor.InstanceId, out var existing)
            && !ReferenceEquals(existing.Connection, connection))
        {
            _ = existing.Connection.DisposeAsync().AsTask();
        }
        _state.Children[registration.Descriptor.InstanceId] = child;
        _state.KnownChildTypes[registration.Descriptor.InstanceId] =
            registration.Descriptor.InstanceType;
        _state.PendingChildLaunches.TryRemove(registration.Descriptor.InstanceId, out _);

        _logger.LogInformation(
            "子实例已注册：{InstanceType} {InstanceId}，管道 {PipeName}",
            registration.Descriptor.InstanceType,
            registration.Descriptor.InstanceId,
            registration.Descriptor.PipeName);
        return InstanceIpcEnvelope.Response(request, _context.InstanceId);
    }

    private InstanceIpcEnvelope HandleInstanceUnregister(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        if (connection.RemoteDescriptor is { } descriptor)
        {
            _state.Children.TryRemove(descriptor.InstanceId, out _);
            _state.KnownChildTypes.TryRemove(descriptor.InstanceId, out _);
            _relativeMouseMessageHandler.RemoveTarget(descriptor.InstanceId);
        }
        return InstanceIpcEnvelope.Response(request, _context.InstanceId);
    }

    /// <summary>
    /// 递归读取子实例树；单个子实例不可用时保留其已知描述，不中断整棵树的响应。
    /// </summary>
    private async Task<InstanceTreeNode> BuildInstanceTreeAsync(
        InstanceConnection requester,
        CancellationToken cancellationToken)
    {
        var children = new List<InstanceTreeNode>();
        foreach (var child in _state.Children.Values.ToArray())
        {
            if (ReferenceEquals(child.Connection, requester))
            {
                children.Add(new InstanceTreeNode { Instance = child.Descriptor });
                continue;
            }

            try
            {
                var response = await child.Connection.SendRequestAsync(
                    InstanceOperations.InstanceGetTree,
                    _context.InstanceId,
                    null,
                    ChildTreeRequestTimeout,
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
            Instance = _context.ToDescriptor(),
            Children = children.ToArray()
        };
    }
}
