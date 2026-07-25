using System;
using System.Collections.Concurrent;

namespace BetterGenshinImpact.Service.Instance.MessageHandlers;

/// <summary>
/// 保存实例消息处理器与 <see cref="InstanceService"/> 共同使用的连接状态。
/// 集中定义这些集合可以避免处理器直接依赖整个服务类。
/// </summary>
internal sealed class InstanceMessageState
{
    internal ConcurrentDictionary<string, ChildInstanceConnection> Children { get; } =
        new(StringComparer.Ordinal);

    internal ConcurrentDictionary<string, PendingChildLaunch> PendingChildLaunches { get; } =
        new(StringComparer.Ordinal);

    internal ConcurrentDictionary<string, BetterGiInstanceType> KnownChildTypes { get; } =
        new(StringComparer.Ordinal);
}

/// <summary>
/// 已注册子实例的描述信息及其 IPC 连接。
/// </summary>
internal sealed record ChildInstanceConnection(
    InstanceDescriptor Descriptor,
    InstanceConnection Connection);

/// <summary>
/// 主实例主动发起、等待子实例完成注册的启动记录。
/// </summary>
internal sealed record PendingChildLaunch(
    BetterGiInstanceType InstanceType,
    DateTimeOffset CreatedAt);
