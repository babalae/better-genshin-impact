namespace BetterGenshinImpact.Service.Instance.MessageHandlers;

/// <summary>
/// 子实例向父实例注册时携带的身份信息。
/// </summary>
internal sealed class InstanceRegisterRequest
{
    public string ParentInstanceId { get; init; } = string.Empty;

    public InstanceDescriptor Descriptor { get; init; } = new();
}

/// <summary>
/// 相对鼠标转发订阅操作的响应状态。
/// </summary>
internal sealed class RelativeMouseState
{
    public bool IsSubscribed { get; init; }
}
