namespace BetterGenshinImpact.Service.ExternalAccess.Logging;

/// <summary>
/// 通知 WebSocket 客户端其消费过慢而被丢弃的日志数量。
/// </summary>
public sealed class ExternalLogDroppedDto
{
    /// <summary>
    /// 消息类型。
    /// </summary>
    public string Type { get; init; } = "dropped";

    /// <summary>
    /// 自上次通知以来被丢弃的日志数量。
    /// </summary>
    public required long Count { get; init; }
}
