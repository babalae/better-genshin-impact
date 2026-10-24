namespace BetterGenshinImpact.Model;

/// <summary>
/// 云原神网页版管理窗口中的实例基础信息
/// </summary>
/// <param name="Name">实例名</param>
/// <param name="IsRunning">刷新时是否正在运行</param>
public sealed record CloudWebInstanceItem(string Name, bool IsRunning)
{
    /// <summary>包含进程运行状态的简短实例名称。</summary>
    public string DisplayName => IsRunning ? $"{Name}（运行中）" : Name;
}
