using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Remote;

namespace BetterGenshinImpact.Service.Instance.MessageHandlers;

/// <summary>
/// pulonia.run.* 请求的处理：子实例接受主实例的整次下发与停止，主实例接收子实例的运行状态上报。
/// 方向规则：submit、cancel 只接受来自主实例的请求，changed 只接受来自已登记子实例的上报；子实例之间、子实例到主实例都不能下发。
/// submit 在子实例固定快照、入队后才响应，准备失败的原因随失败响应返回；cancel 只发出取消、不等待运行结束；changed 只更新主实例的显示。
/// 处理方法直接接收连接登记的远端端点，便于在不建立命名管道的情况下测试方向校验。
/// </summary>
internal sealed class PuloniaRunMessageHandler
{
    /// <summary>当前实例身份。</summary>
    private readonly InstanceContext _context;

    /// <summary>子实例侧的下发宿主，首次使用时才从容器取得。</summary>
    private readonly Func<PuloniaRunDispatchHost> _host;

    /// <summary>主实例侧的下发客户端，首次使用时才从容器取得。</summary>
    private readonly Func<PuloniaRunDispatcher> _dispatcher;

    /// <summary>
    /// 创建处理器。
    /// </summary>
    /// <param name="context">当前实例身份</param>
    /// <param name="host">子实例侧的下发宿主</param>
    /// <param name="dispatcher">主实例侧的下发客户端</param>
    internal PuloniaRunMessageHandler(InstanceContext context, Func<PuloniaRunDispatchHost> host,
        Func<PuloniaRunDispatcher> dispatcher)
    {
        _context = context;
        _host = host;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// 子实例：校验来自主实例后接受下发，入队成功时回复运行 ID。
    /// </summary>
    /// <param name="remote">连接登记的远端端点；尚未登记时为空</param>
    /// <param name="request">请求信封</param>
    /// <param name="ct">连接生命周期令牌</param>
    /// <returns>含运行 ID 的响应</returns>
    internal async Task<InstanceIpcEnvelope> HandleSubmitAsync(InstanceEndpoint? remote, InstanceIpcEnvelope request,
        CancellationToken ct)
    {
        RequireCommandFromRoot(remote);
        var submit = request.Data?.ToObject<PuloniaRunSubmitRequest>(InstanceIpcProtocol.Serializer)
                     ?? throw new ArgumentException("Pulonia 下发请求缺少数据。");
        var response = await _host().SubmitAsync(submit, ct).ConfigureAwait(false);
        return InstanceIpcEnvelope.Response(request, response);
    }

    /// <summary>
    /// 子实例：校验来自主实例后停止一个主实例下发的运行；只发出取消即响应。
    /// </summary>
    /// <param name="remote">连接登记的远端端点；尚未登记时为空</param>
    /// <param name="request">请求信封</param>
    /// <param name="ct">连接生命周期令牌</param>
    /// <returns>空响应</returns>
    internal async Task<InstanceIpcEnvelope> HandleCancelAsync(InstanceEndpoint? remote, InstanceIpcEnvelope request,
        CancellationToken ct)
    {
        RequireCommandFromRoot(remote);
        var cancel = request.Data?.ToObject<PuloniaRunCancelRequest>(InstanceIpcProtocol.Serializer)
                     ?? throw new ArgumentException("Pulonia 停止请求缺少数据。");
        await _host().CancelAsync(cancel, ct).ConfigureAwait(false);
        return InstanceIpcEnvelope.Response(request);
    }

    /// <summary>
    /// 主实例：记录已登记子实例上报的下发运行状态。
    /// </summary>
    /// <param name="remote">连接登记的远端端点；尚未登记时为空</param>
    /// <param name="request">请求信封</param>
    /// <returns>空响应</returns>
    internal InstanceIpcEnvelope HandleChanged(InstanceEndpoint? remote, InstanceIpcEnvelope request)
    {
        var source = RequireReportFromChild(remote);
        var report = request.Data?.ToObject<PuloniaRunChangedReport>(InstanceIpcProtocol.Serializer)
                     ?? throw new ArgumentException("Pulonia 运行状态上报缺少数据。");
        _dispatcher().ReceiveChanged(source, report);
        return InstanceIpcEnvelope.Response(request);
    }

    /// <summary>
    /// 下发与停止只能由主实例发给子实例。
    /// </summary>
    /// <param name="remote">连接登记的远端端点</param>
    /// <exception cref="InvalidOperationException">本实例是主实例，或请求不是来自主实例</exception>
    private void RequireCommandFromRoot(InstanceEndpoint? remote)
    {
        if (_context.InstanceType == BetterGiInstanceType.Primary
            || remote?.InstanceType != BetterGiInstanceType.Primary)
            throw new InvalidOperationException("只有主实例可以向子实例下发或停止 Pulonia 运行。");
    }

    /// <summary>
    /// 运行状态上报只能由已登记的子实例发给主实例。
    /// </summary>
    /// <param name="remote">连接登记的远端端点</param>
    /// <returns>发送上报的子实例端点</returns>
    /// <exception cref="InvalidOperationException">本实例不是主实例，或连接尚未登记、来自主实例</exception>
    private InstanceEndpoint RequireReportFromChild(InstanceEndpoint? remote)
    {
        if (_context.InstanceType != BetterGiInstanceType.Primary)
            throw new InvalidOperationException("只有主实例可以接收 Pulonia 运行状态上报。");
        if (remote is null)
            throw new InvalidOperationException("当前管道连接尚未完成登记。");
        if (remote.InstanceType == BetterGiInstanceType.Primary)
            throw new InvalidOperationException("Pulonia 运行状态上报只能来自子实例。");
        return remote;
    }
}
