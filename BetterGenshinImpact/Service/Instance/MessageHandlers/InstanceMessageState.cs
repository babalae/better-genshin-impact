using System;
using System.Collections.Concurrent;

namespace BetterGenshinImpact.Service.Instance.MessageHandlers;

/// <summary>
/// 保存实例消息处理器与 <see cref="InstanceService"/> 共同使用的连接状态。
/// 集中定义这些集合可以避免处理器直接依赖整个服务类。
/// </summary>
internal sealed class InstanceMessageState
{
    internal object RegistrationLock { get; } = new();

    internal ConcurrentDictionary<int, RegisteredInstanceConnection> BetterGiConnectionsBySession { get; } =
        new();

    internal ConcurrentDictionary<int, RegisteredInstanceConnection> WebViewConnectionsByProcessId { get; } =
        new();
}

/// <summary>
/// 已注册子实例的描述信息及其 IPC 连接。
/// </summary>
internal sealed record RegisteredInstanceConnection(
    InstanceEndpoint Endpoint,
    InstanceConnection Connection);

// v1 中主实例会创建“等待子实例完成注册”的启动记录。
// v2 不再预先创建启动记录；连接关系由根管道、用途和实际 Windows Session 决定。
