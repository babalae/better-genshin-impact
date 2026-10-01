namespace BetterGenshinImpact.Model;

/// <summary>
/// 首页「云原神网页版」卡片中的实例列表项
/// </summary>
/// <param name="Name">实例名</param>
/// <param name="IsRunning">刷新时是否正在运行</param>
public sealed record CloudWebInstanceItem(string Name, bool IsRunning)
{
    public string DisplayName => IsRunning ? $"{Name}（运行中）" : Name;
}
