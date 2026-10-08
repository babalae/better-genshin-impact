using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.Instance;

/// <summary>
/// 命名管道连接的处理方。
/// <para>
/// 根实例由 <see cref="InstanceService"/> 实现，无界面 Worker 由 WorkerIpcService 实现，
/// 使两者共用同一套 <see cref="InstanceConnection"/> 收发框架与帧协议。
/// </para>
/// </summary>
internal interface IInstanceConnectionOwner
{
    /// <summary>
    /// 是否需要向该连接转发本地相对鼠标
    /// </summary>
    bool IsGameMouseModeEnabled { get; }

    /// <summary>
    /// 处理一条 JSON 控制请求，返回 null 表示不需要回复
    /// </summary>
    Task<InstanceIpcEnvelope?> HandleRequestAsync(
        InstanceConnection connection,
        InstanceIpcEnvelope request,
        CancellationToken cancellationToken);

    /// <summary>
    /// 处理相对鼠标批次，返回是否已被前台游戏消费
    /// </summary>
    bool ReceiveRelativeMouseBatch(
        InstanceConnection connection,
        ulong firstSequence,
        IReadOnlyList<RelativeMouseSample> samples);

    /// <summary>
    /// 处理相对鼠标批次结果
    /// </summary>
    void ReceiveRelativeMouseResult(
        InstanceConnection connection,
        RelativeMouseResult result);

    /// <summary>
    /// 连接接收循环已结束
    /// </summary>
    void ConnectionClosed(InstanceConnection connection);
}
