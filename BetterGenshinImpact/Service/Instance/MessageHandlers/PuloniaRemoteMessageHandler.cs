using System;
using System.IO;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Remote;

namespace BetterGenshinImpact.Service.Instance.MessageHandlers;

/// <summary>
/// pulonia.node.* 请求的处理：子实例接受主实例派发的节点启动与取消，主实例接收子实例的完成事件与结束回报。
/// 处理方法都快速返回，节点执行在后台进行，不阻塞连接的接收循环
/// </summary>
internal sealed class PuloniaRemoteMessageHandler
{
    /// <summary>
    /// 当前实例身份
    /// </summary>
    private readonly InstanceContext _context;

    /// <summary>
    /// 子实例侧节点执行宿主，首次使用时才创建
    /// </summary>
    private readonly Func<PuloniaRemoteNodeHost> _nodeHost;

    /// <summary>
    /// 主实例侧派发代理，首次使用时才创建
    /// </summary>
    private readonly Func<PuloniaRemoteExecutionBroker> _broker;

    /// <summary>
    /// 创建处理器
    /// </summary>
    /// <param name="context">当前实例身份</param>
    /// <param name="nodeHost">子实例侧节点执行宿主</param>
    /// <param name="broker">主实例侧派发代理</param>
    internal PuloniaRemoteMessageHandler(InstanceContext context, Func<PuloniaRemoteNodeHost> nodeHost,
        Func<PuloniaRemoteExecutionBroker> broker)
    {
        _context = context;
        _nodeHost = nodeHost;
        _broker = broker;
    }

    /// <summary>
    /// 子实例：校验来自主实例后接受节点，立即确认
    /// </summary>
    internal InstanceIpcEnvelope HandleNodeStart(InstanceConnection connection, InstanceIpcEnvelope request)
    {
        RequireCommandFromRoot(connection);
        var start = request.Data?.ToObject<PuloniaRemoteNodeStart>(InstanceIpcProtocol.Serializer)
                    ?? throw new ArgumentException("Pulonia 节点启动请求缺少数据。");
        Wrap(() => _nodeHost().Start(start));
        return InstanceIpcEnvelope.Response(request);
    }

    /// <summary>
    /// 子实例：校验来自主实例后取消节点
    /// </summary>
    internal InstanceIpcEnvelope HandleNodeCancel(InstanceConnection connection, InstanceIpcEnvelope request)
    {
        RequireCommandFromRoot(connection);
        var cancel = request.Data?.ToObject<PuloniaRemoteNodeCancel>(InstanceIpcProtocol.Serializer)
                     ?? throw new ArgumentException("Pulonia 节点取消请求缺少数据。");
        _nodeHost().Cancel(cancel.ExecutionId);
        return InstanceIpcEnvelope.Response(request);
    }

    /// <summary>
    /// 主实例：把完成事件交给协调器落盘，成功后才确认；落盘被拒绝时返回失败，子实例的执行器随之失败
    /// </summary>
    internal async Task<InstanceIpcEnvelope> HandleNodeReportAsync(InstanceConnection connection, InstanceIpcEnvelope request)
    {
        var source = RequireReportFromChild(connection);
        var report = request.Data?.ToObject<PuloniaRemoteNodeReport>(InstanceIpcProtocol.Serializer)
                     ?? throw new ArgumentException("Pulonia 完成事件缺少数据。");
        try
        {
            await _broker().ReceiveReportAsync(source.ProcessId, report).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            // 取消类异常会被接收循环当作断线处理，这里转成可回报的超时
            throw new TimeoutException("完成事件落盘超时。", exception);
        }
        catch (Exception exception) when (exception is not (ArgumentException or InvalidOperationException
                                              or IOException or TimeoutException))
        {
            // 校验与序列化异常也要转成失败响应，未列出的异常类型会终止连接的接收循环
            throw new InvalidOperationException(exception.Message, exception);
        }

        return InstanceIpcEnvelope.Response(request);
    }

    /// <summary>
    /// 主实例：记录节点结束回报
    /// </summary>
    internal InstanceIpcEnvelope HandleNodeFinished(InstanceConnection connection, InstanceIpcEnvelope request)
    {
        var source = RequireReportFromChild(connection);
        var finished = request.Data?.ToObject<PuloniaRemoteNodeFinished>(InstanceIpcProtocol.Serializer)
                       ?? throw new ArgumentException("Pulonia 节点结束回报缺少数据。");
        _broker().ReceiveFinished(source.ProcessId, finished);
        return InstanceIpcEnvelope.Response(request);
    }

    /// <summary>
    /// 节点控制请求只能由主实例发给子实例
    /// </summary>
    private void RequireCommandFromRoot(InstanceConnection connection)
    {
        if (_context.InstanceType == BetterGiInstanceType.Primary
            || connection.RemoteEndpoint?.InstanceType != BetterGiInstanceType.Primary)
            throw new InvalidOperationException("只有根实例可以向子实例派发 Pulonia 节点。");
    }

    /// <summary>
    /// 节点回报只能由已登记的子实例发给主实例
    /// </summary>
    private InstanceEndpoint RequireReportFromChild(InstanceConnection connection)
    {
        if (_context.InstanceType != BetterGiInstanceType.Primary)
            throw new InvalidOperationException("只有根实例可以接收 Pulonia 节点回报。");
        return connection.RemoteEndpoint ?? throw new InvalidOperationException("当前管道连接尚未完成登记。");
    }

    /// <summary>
    /// 把宿主的校验异常统一转换为可回报的失败
    /// </summary>
    private static void Wrap(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is not (ArgumentException or InvalidOperationException))
        {
            throw new InvalidOperationException(exception.Message, exception);
        }
    }
}
